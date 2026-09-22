using MemoriaNote.Application;
using MemoriaNote.Domain;

namespace MemoriaNote.Cli
{
    internal static class PageTargetCommandParser
    {
        internal static bool TryCreateSelector(
            string pageName,
            string pageId,
            out PageSelector selector,
            out CliCommandResult failure)
        {
            var hasName = pageName != null;
            var hasPageId = pageId != null;
            if (!hasName && !hasPageId)
            {
                selector = null;
                failure = CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "Specify a page name or --id.");
                return false;
            }
            if (hasName && hasPageId)
            {
                selector = null;
                failure = CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "A page name and --id cannot be used together.");
                return false;
            }

            if (hasName)
            {
                if (string.IsNullOrWhiteSpace(pageName))
                {
                    selector = null;
                    failure = CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "The page name cannot be empty or whitespace.");
                    return false;
                }

                selector = PageSelector.FromName(pageName);
                failure = null;
                return true;
            }

            if (!PageSelector.TryFromPageId(pageId, out selector))
            {
                failure = CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "Page ID must be a complete UUID or a prefix of 4 to 32 " +
                    "hexadecimal characters.");
                return false;
            }

            failure = null;
            return true;
        }

        internal static CliCommandResult ToResolutionFailure(
            PageTargetResolutionStatus status,
            PageSelector selector)
        {
            return status switch
            {
                PageTargetResolutionStatus.OwnerNotFound => CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    "The target notebook was not found."),
                PageTargetResolutionStatus.PageNotFound => CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    selector.IsName
                        ? "No page matched the supplied name."
                        : "No page matched the supplied Page ID."),
                PageTargetResolutionStatus.Conflict => CliCommandResult.Failure(
                    CliErrorKind.Conflict,
                    selector.IsName
                        ? "More than one page matched the supplied name. " +
                            "Use --id to select one."
                        : "The Page ID prefix matched more than one page. " +
                            "Specify more characters."),
                _ => throw new System.ArgumentOutOfRangeException(nameof(status))
            };
        }
    }
}
