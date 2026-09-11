using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Applies page input rules that do not require I/O.
    /// </summary>
    public sealed class PageValidationPolicy
    {
        /// <summary>
        /// Validates a page name.
        /// </summary>
        /// <param name="name">The page name.</param>
        /// <returns>The machine-readable validation errors.</returns>
        public IReadOnlyList<PageErrorCode> ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Array.AsReadOnly(new[] { PageErrorCode.NameRequired });

            return Array.Empty<PageErrorCode>();
        }

        /// <summary>
        /// Validates page text using the current unrestricted text policy.
        /// </summary>
        /// <param name="text">The page text.</param>
        /// <returns>The machine-readable validation errors.</returns>
        public IReadOnlyList<PageErrorCode> ValidateText(string text)
        {
            return Array.Empty<PageErrorCode>();
        }
    }
}
