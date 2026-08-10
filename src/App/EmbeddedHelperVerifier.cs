using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using MacBookEco.Core;

namespace MacBookEco.App
{
    /// <summary>
    /// Verifies an adjacent helper against the exact bytes embedded in the
    /// application and retains a deny-write/delete handle for the caller.
    /// </summary>
    internal sealed class EmbeddedHelperVerifier
    {
        public FileStream OpenVerifiedHelper(
            string executablePath,
            string resourceName,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException(
                    "A helper executable path is required.",
                    nameof(executablePath));
            }

            FileStream helperStream = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            try
            {
                using (Stream embeddedStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName))
                {
                    if (embeddedStream == null)
                    {
                        throw new InvalidDataException(
                            "This build does not contain the "
                            + displayName
                            + " integrity resource.");
                    }

                    if (helperStream.Length != embeddedStream.Length)
                    {
                        throw new InvalidDataException(
                            displayName
                            + " does not match this application build.");
                    }

                    byte[] actualHash;
                    byte[] expectedHash;
                    using (SHA256 algorithm = SHA256.Create())
                    {
                        actualHash = algorithm.ComputeHash(helperStream);
                    }

                    using (SHA256 algorithm = SHA256.Create())
                    {
                        expectedHash = algorithm.ComputeHash(embeddedStream);
                    }

                    if (!FixedTimeComparer.AreEqual(actualHash, expectedHash))
                    {
                        throw new InvalidDataException(
                            displayName
                            + " failed the embedded SHA-256 integrity check.");
                    }
                }

                return helperStream;
            }
            catch
            {
                helperStream.Dispose();
                throw;
            }
        }

    }
}
