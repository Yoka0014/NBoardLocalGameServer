using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class RequestExtensions
    {
        /// <summary>
        /// Reads a multipart form, turning the size-limit exception ASP.NET Core throws when a part
        /// exceeds <see cref="Microsoft.AspNetCore.Http.Features.FormOptions.MultipartBodyLengthLimit"/>
        /// into a readable 413 response instead of letting it propagate as an unhandled 500.
        /// </summary>
        public static async Task<(IFormCollection? Form, IResult? Error)> TryReadFormAsync(this HttpRequest request)
        {
            try
            {
                return (await request.ReadFormAsync(), null);
            }
            catch (InvalidDataException ex)
            {
                var maxMb = ServeHost.MaxUploadBytes / 1_000_000;
                return (null, Results.Json(
                    $"Upload is too large (limit is {maxMb} MB): {ex.Message}",
                    statusCode: StatusCodes.Status413PayloadTooLarge));
            }
        }
    }
}
