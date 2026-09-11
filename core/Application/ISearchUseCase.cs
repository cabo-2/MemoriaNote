using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Searches one note or an ordered collection of notes.
    /// </summary>
    public interface ISearchUseCase
    {
        /// <summary>
        /// Executes the specified search request.
        /// </summary>
        /// <param name="request">The immutable search request.</param>
        /// <param name="token">The cancellation token for all repository operations.</param>
        /// <returns>The ordered result slice and its unpaged total count.</returns>
        Task<SearchPage> SearchAsync(SearchRequest request, CancellationToken token);
    }
}
