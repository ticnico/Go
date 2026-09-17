using GoldenBullet.Web.Models.Errors;

namespace GoldenBullet.Web.Tests.Utils;

public class ApiErrorResponse
{
    public ApiError? Content { get; set; }
    public required HttpResponseMessage Response { get; set; }
}
