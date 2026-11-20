using MemoriaNote.Application;
using MemoriaNote.Domain;
using System.Threading;
using System.Threading.Tasks;

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

        internal static Task<NotebookOperationResult<NotebookPage>> ResolveAsync(
            INotebookPageApplication pages,
            PageSelector selector,
            CancellationToken cancellationToken)
        {
            return selector.IsName
                ? pages.GetByNameAsync(selector.Name, cancellationToken)
                : pages.ResolveByIdPrefixAsync(
                    selector.PageIdPrefix,
                    cancellationToken);
        }

        internal static CliCommandResult ToResolutionFailure(
            NotebookFailure failure,
            PageSelector selector)
        {
            if (failure == null)
                throw new System.ArgumentNullException(nameof(failure));

            return failure.Code switch
            {
                "page.not_found" => CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    selector.IsName
                        ? "No page matched the supplied name."
                        : "No page matched the supplied Page ID."),
                "page.ambiguous_id_prefix" => CliCommandResult.Failure(
                    CliErrorKind.Conflict,
                    "The Page ID prefix matched more than one page. " +
                    "Specify more characters."),
                _ => NotebookApplicationFailureMapper.ToCliResult(failure)
            };
        }
    }
}
