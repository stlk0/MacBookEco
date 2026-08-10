using System;
using System.Collections.Generic;

namespace MacBookEco.Tests
{
    /// <summary>
    /// One assertion vocabulary and one runner for every suite in this
    /// repository.
    ///
    /// The lightweight runner keeps these .NET Framework harnesses directly
    /// executable in the same constrained Windows environments as the product.
    /// A contributor should still learn one assertion vocabulary and receive
    /// one consistent per-case report from every host-safe behavioral suite.
    /// </summary>
    internal sealed class TestCase
    {
        public TestCase(string name, Action action)
        {
            Name = name;
            Action = action;
        }

        public string Name { get; private set; }

        public Action Action { get; private set; }
    }

    internal static class TestSuite
    {
        /// <summary>
        /// Runs every case, reports each one, and returns a process exit code.
        /// Deliberately does not stop at the first failure: a run that reports
        /// one failure out of twenty is more useful than one that reports the
        /// first and hides the rest.
        /// </summary>
        public static int Run(string title, IList<TestCase> cases)
        {
            if (!string.IsNullOrEmpty(title))
            {
                Console.WriteLine(title);
                Console.WriteLine(new string('=', title.Length));
            }

            int passed = 0;
            int failed = 0;
            for (int index = 0; index < cases.Count; index++)
            {
                TestCase test = cases[index];
                try
                {
                    test.Action();
                    passed++;
                    Console.WriteLine("[PASS] " + test.Name);
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.WriteLine("[FAIL] " + test.Name);
                    Console.WriteLine("       " + exception);
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                string.Format("{0} passed, {1} failed.", passed, failed));
            return failed == 0 ? 0 : 1;
        }
    }

    internal static class Check
    {
        /// <summary>
        /// The message-carrying form. Prefer it wherever the assertion is
        /// about a domain rule rather than a value, because the message is
        /// what a contributor reads when the build breaks.
        /// </summary>
        public static void That(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void True(bool value)
        {
            That(value, "Expected true, received false.");
        }

        public static void False(bool value)
        {
            That(!value, "Expected false, received true.");
        }

        public static void NotNull(object value)
        {
            That(value != null, "Expected a non-null value.");
        }

        public static void Equal<T>(T expected, T actual)
        {
            That(
                EqualityComparer<T>.Default.Equals(expected, actual),
                string.Format("Expected <{0}>, received <{1}>.", expected, actual));
        }

        public static void Near(double expected, double actual, double tolerance)
        {
            That(
                Math.Abs(expected - actual) <= tolerance,
                string.Format(
                    "Expected <{0}> +/- <{1}>, received <{2}>.",
                    expected,
                    tolerance,
                    actual));
        }

        public static void BytesEqual(byte[] expected, byte[] actual)
        {
            That(
                expected != null && actual != null
                    && expected.Length == actual.Length,
                "Byte arrays have different lengths.");
            for (int index = 0; index < expected.Length; index++)
            {
                That(
                    expected[index] == actual[index],
                    string.Format(
                        "Byte arrays differ at offset {0}: expected 0x{1:X2}, received 0x{2:X2}.",
                        index,
                        expected[index],
                        actual[index]));
            }
        }

        public static void Throws<TException>(Action action)
            where TException : Exception
        {
            Throws<TException>(
                action,
                "Expected exception " + typeof(TException).FullName
                    + " was not thrown.");
        }

        public static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }
    }
}
