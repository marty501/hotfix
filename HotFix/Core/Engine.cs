using System;
using System.Runtime.CompilerServices;
using HotFix.Transport;
using HotFix.Utilities;

namespace HotFix.Core
{
    public class Engine
    {
        public static IClock Clock { get; set; }
        public Func<IConfiguration, ITransport> Transports { get; set; }
        public State State { get; private set; }

        public Engine()
        {
            Clock = new RealTimeClock();
            Transports = c => new TcpTransport(c.Host, c.Port);
        }

        public void Run(IConfiguration configuration)
        {
            var state = State = new State
            {
                InboundSeqNum = configuration.InboundSeqNum,
                OutboundSeqNum = configuration.OutboundSeqNum,
                InboundTimestamp = Clock.Time,
                OutboundTimestamp = Clock.Time
            };

            var transport = Transports(configuration);
            var channel = new Channel(transport);

            var inbound = new FIXMessage();
            var outbound = new FIXMessageWriter(1024, configuration.Version);

            HandleLogon(configuration, state, channel, inbound, outbound);

            while (true)
            {
                if (inbound.Valid)
                {
                    if (!inbound[8].Is(configuration.Version)) throw new EngineException("Unexpected begin string received");
                    if (!inbound[49].Is(configuration.Target)) throw new EngineException("Unexpected comp id received");
                    if (!inbound[56].Is(configuration.Sender)) throw new EngineException("Unexpected comp id received");

                    if (inbound[34].Is(state.InboundSeqNum))
                    {
                        state.Synchronizing = false;
                        state.TestRequestPending = false;

                        // Process message
                        Console.WriteLine("Processing: " + inbound[35].AsString);

                        switch (inbound[35].AsString)
                        {
                            case "1":
                                HandleTestRequest(configuration, state, channel, inbound, outbound);
                                break;
                            case "2":
                                HandleResendRequest(configuration, state, channel, inbound, outbound);
                                break;
                            case "4":
                                HandleSequenceReset(configuration, state, channel, inbound, outbound);
                                break;
                            default:
                                break;
                        }

                        state.InboundSeqNum++;
                        state.InboundTimestamp = Clock.Time;
                    }
                    else
                    {
                        if (inbound[34].AsLong < state.InboundSeqNum) throw new EngineException("Sequence number too low");
                        if (inbound[34].AsLong > state.InboundSeqNum) SendResendRequest(configuration, state, channel, outbound);
                    }
                }

                if (Clock.Time - state.OutboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval))
                {
                    SendHeartbeat(configuration, state, channel, outbound);
                }

                if (Clock.Time - state.InboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval * 1.2))
                {
                    if (Clock.Time - state.InboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval * 2))
                    {
                        throw new EngineException("Did not receive any messages for too long");
                    }

                    if (!state.TestRequestPending)
                    {
                        SendTestRequest(configuration, state, channel, outbound);
                        state.TestRequestPending = true;
                    }
                }

                inbound.Clear();

                channel.Read(inbound);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendHeartbeat(IConfiguration configuration, State state, Channel channel, FIXMessageWriter outbound)
        {
            outbound.Prepare("0");
            outbound.Set(34, state.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Build();

            Send(state, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendTestRequest(IConfiguration configuration, State state, Channel channel, FIXMessageWriter outbound)
        {
            outbound.Prepare("1");
            outbound.Set(34, state.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(112, Clock.Time.Ticks);
            outbound.Build();

            Send(state, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendResendRequest(IConfiguration configuration, State state, Channel channel, FIXMessageWriter outbound)
        {
            if (state.Synchronizing) return;

            outbound.Prepare("2");
            outbound.Set(34, state.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(7, state.InboundSeqNum);
            outbound.Set(16, 0);
            outbound.Build();

            Send(state, channel, outbound);

            state.Synchronizing = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleTestRequest(IConfiguration configuration, State state, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Prepare and send a heartbeat (with the test request id)
            outbound.Prepare("0");
            outbound.Set(34, state.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(112, inbound[112].AsString);
            outbound.Build();

            Send(state, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleResendRequest(IConfiguration configuration, State state, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Validate request
            if (!inbound[16].Is(0L)) throw new EngineException("Unsupported resend request received (partial gap fills are not supported)");

            // Prepare and send a gap fill message
            outbound.Prepare("4");
            outbound.Set(34, inbound[7].AsLong);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(123, "Y");
            outbound.Set(36, state.OutboundSeqNum);
            outbound.Build();

            Send(state, channel, outbound);

            // HACK
            state.OutboundSeqNum--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleSequenceReset(IConfiguration configuration, State state, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Validate request
            if (!inbound.Contains(123) || !inbound[123].Is("Y")) throw new Exception("Unsupported sequence reset received (hard reset)");
            if (inbound[36].AsLong <= state.InboundSeqNum) throw new Exception("Invalid sequence reset received (bad new seq num)");

            // Accept the new sequence number
            state.InboundSeqNum = inbound[36].AsLong;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send(State state, Channel channel, FIXMessageWriter message)
        {
            channel.Write(message);

            state.OutboundSeqNum++;
            state.OutboundTimestamp = Clock.Time;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleLogon(IConfiguration configuration, State state, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            outbound.Prepare("A");
            outbound.Set(34, state.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(108, configuration.HeartbeatInterval);
            outbound.Set(98, 0);
            outbound.Set(141, "Y");
            outbound.Build();

            Send(state, channel, outbound);

            while (Clock.Time - state.OutboundTimestamp < TimeSpan.FromSeconds(10))
            {
                channel.Read(inbound);

                if (inbound.Valid)
                {
                    if (!inbound[35].Is("A")) throw new EngineException("Unexpected first message received (expected a logon)");
                    if (!inbound[108].Is(configuration.HeartbeatInterval)) throw new EngineException("Unexpected heartbeat interval received");
                    if (!inbound[ 98].Is(0)) throw new EngineException("Unexpected encryption method received");
                    if (!inbound[141].Is("Y")) throw new EngineException("Unexpected reset on logon received");

                    return;
                }
            }

            throw new EngineException("Logon response not received on time");
        }
    }

    public class EngineException : Exception
    {
        public EngineException()
        {
            
        }

        public EngineException(string message) : base(message)
        {
            
        }
    }
}