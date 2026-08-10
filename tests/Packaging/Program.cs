using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace MacBookEco.Tests.Packaging
{
    internal static class Program
    {
        private const string AdminResourceName = "MacBookEco.Admin.exe";
        private const string CompanyName = "MacBook Eco contributors";
        private const string Copyright =
            "Copyright (c) 2026 MacBook Eco contributors";
        private const string ProductName = "MacBook Eco";
        private const string WatchdogResourceName = "MacBookEco.Watchdog.exe";

        private static int Main(string[] arguments)
        {
            if (arguments.Length != 4)
            {
                Console.Error.WriteLine(
                    "Usage: MacBookEco.PackagingTests.exe "
                    + "<app.exe> <admin.exe> <watchdog.exe> <version>");
                return 2;
            }

            try
            {
                string applicationPath = Path.GetFullPath(arguments[0]);
                string adminPath = Path.GetFullPath(arguments[1]);
                string watchdogPath = Path.GetFullPath(arguments[2]);
                string informationalVersion = arguments[3];
                Assembly application = Assembly.LoadFile(applicationPath);
                VerifyResourceMatchesFile(
                    application,
                    AdminResourceName,
                    adminPath);
                VerifyResourceMatchesFile(
                    application,
                    WatchdogResourceName,
                    watchdogPath);
                VerifyMetadata(
                    applicationPath,
                    informationalVersion,
                    "MacBookEco.exe");
                VerifyMetadata(
                    adminPath,
                    informationalVersion,
                    "MacBookEco.Admin.exe");
                VerifyMetadata(
                    watchdogPath,
                    informationalVersion,
                    "MacBookEco.Watchdog.exe");

                Console.WriteLine("Packaging integrity PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Packaging integrity FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void VerifyMetadata(
            string filePath,
            string informationalVersion,
            string originalFilename)
        {
            string semanticCore = informationalVersion;
            int suffix = semanticCore.IndexOf('-');
            if (suffix >= 0)
            {
                semanticCore = semanticCore.Substring(0, suffix);
            }

            Version parsed = new Version(semanticCore);
            string expectedFileVersion = parsed.Major
                + "."
                + parsed.Minor
                + "."
                + parsed.Build
                + ".0";
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(filePath);
            if (!string.Equals(
                    version.CompanyName,
                    CompanyName,
                    StringComparison.Ordinal)
                || !string.Equals(
                    version.FileVersion,
                    expectedFileVersion,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(version.FileDescription)
                || !string.Equals(
                    version.LegalCopyright,
                    Copyright,
                    StringComparison.Ordinal)
                || !string.Equals(
                    version.OriginalFilename,
                    originalFilename,
                    StringComparison.Ordinal)
                || !string.Equals(
                    version.ProductName,
                    ProductName,
                    StringComparison.Ordinal)
                || !string.Equals(
                    version.ProductVersion,
                    informationalVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    Path.GetFileName(filePath)
                    + " signing metadata is incomplete or does not match VERSION.");
            }
        }

        private static void VerifyResourceMatchesFile(
            Assembly application,
            string resourceName,
            string filePath)
        {
            using (Stream resource = application.GetManifestResourceStream(
                resourceName))
            {
                if (resource == null)
                {
                    throw new InvalidDataException(
                        "The application does not embed " + resourceName + ".");
                }

                byte[] expected = ComputeSha256(resource);
                byte[] actual;
                using (FileStream file = File.OpenRead(filePath))
                {
                    actual = ComputeSha256(file);
                }

                if (!BytesEqual(expected, actual))
                {
                    throw new InvalidDataException(
                        "The embedded and adjacent "
                        + resourceName
                        + " files differ.");
                }
            }
        }

        private static byte[] ComputeSha256(Stream stream)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return algorithm.ComputeHash(stream);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
