using System;
using UnityEngine;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>Builds and opens the Troubleshooting links of the diagnostic IDs.</summary>
    internal static class DocLinks
    {
        /// <summary>
        /// The repository's blob root. It must match the repository of the package's documentationUrl. The URL continues with a
        /// reference (a version tag or <see cref="FallbackReference"/>), a slash and <see cref="DiagnosticIds.GetAnchor"/>.
        /// </summary>
        internal const string BlobBaseUrl = "https://github.com/ecanakli/UnityJanitor/blob/";

        /// <summary>The branch used when the package version cannot be read.</summary>
        internal const string FallbackReference = "main";

        /// <summary>A release tag is the package version behind this prefix.</summary>
        internal const string TagPrefix = "v";

        /// <summary>Reads the package version; replaced by tests. Null or empty means it could not be read.</summary>
        internal static Func<string> VersionProvider = ReadPackageVersion;

        /// <summary>Replaced by tests so a click does not open a browser.</summary>
        internal static Action<string> Opener = OpenInBrowser;

        /// <summary>Asks the package manager for the installed version; replaced by tests. Null means it could not be read.</summary>
        internal static Func<string> PackageVersionSource = FindPackageVersion;

        // The answer of the package manager, kept for the domain whether it was a version or a failure.
        private static string s_cachedVersion;
        private static bool s_versionRead;

        /// <summary>
        /// The Troubleshooting URL of an ID, pinned to the release tag of the installed package version
        /// (<c>.../blob/v{version}/Documentation~/Troubleshooting.md#janitor1xx</c>), or to <c>main</c> when the version cannot be read.
        /// Null when the ID is not one of the package's IDs.
        /// </summary>
        internal static string BuildUrl(string diagnosticId)
        {
            var anchor = DiagnosticIds.GetAnchor(diagnosticId);
            if (string.IsNullOrEmpty(anchor))
            {
                return null;
            }

            return BlobBaseUrl + ReferenceName() + "/" + anchor;
        }

        /// <summary>The tag of the installed version, or <see cref="FallbackReference"/> when the version is unknown. Never throws.</summary>
        internal static string ReferenceName()
        {
            string version = null;
            try
            {
                var provider = VersionProvider;
                version = provider == null ? null : provider();
            }
            catch (Exception)
            {
                // An unreadable version is the documented fallback, not an error.
            }

            return string.IsNullOrWhiteSpace(version) ? FallbackReference : TagPrefix + version.Trim();
        }

        /// <summary>Opens the Troubleshooting entry of an ID. Returns false when there is none. Never throws.</summary>
        internal static bool Open(string diagnosticId)
        {
            var url = BuildUrl(diagnosticId);
            if (url == null)
            {
                return false;
            }

            try
            {
                Opener?.Invoke(url);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        /// <summary>Puts the version provider, the package manager lookup and the opener back to their defaults, and forgets the cached version.</summary>
        internal static void ResetSeams()
        {
            VersionProvider = ReadPackageVersion;
            PackageVersionSource = FindPackageVersion;
            Opener = OpenInBrowser;
            s_cachedVersion = null;
            s_versionRead = false;
        }

        // The version comes from the package manager, asked once per domain. A failed read is kept too, so a package that
        // the package manager does not know (copied into Assets) does not cost a lookup for every row and every refresh.
        internal static string ReadPackageVersion()
        {
            if (s_versionRead)
            {
                return s_cachedVersion;
            }

            s_versionRead = true;
            try
            {
                var source = PackageVersionSource;
                var version = source == null ? null : source();
                s_cachedVersion = string.IsNullOrEmpty(version) ? null : version;
            }
            catch (Exception)
            {
                s_cachedVersion = null;
            }

            return s_cachedVersion;
        }

        private static string FindPackageVersion()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DocLinks).Assembly);
            return package == null ? null : package.version;
        }

        private static void OpenInBrowser(string url)
        {
            Application.OpenURL(url);
        }
    }
}
