﻿using System;
using FluentAssertions;
using HotFix.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HotFix.Test.core.field
{
    [TestClass]
    public class checking_the_value_for_long_equality
    {
        [TestMethod]
        public void returns_false_when_the_value_is_not_a_valid_long()
        {
            var field = new Field(System.Text.Encoding.ASCII.GetBytes("34=XXXXX\u0001"), 0, new Segment(3, 5), 9, 0);

            field.Is(12345L).Should().Be(false);
        }

        [TestMethod]
        public void returns_false_when_the_value_is_not_equal_to_the_input()
        {
            var field = new Field(System.Text.Encoding.ASCII.GetBytes("34=54321\u0001"), 0, new Segment(3, 5), 9, 0);

            field.Is(12345L).Should().Be(false);
        }

        [TestMethod]
        public void returns_true_when_the_value_is_equal_to_the_input()
        {
            var field = new Field(System.Text.Encoding.ASCII.GetBytes("34=12345\u0001"), 0, new Segment(3, 5), 9, 0);

            field.Is(12345L).Should().Be(true);
        }
    }
}