using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Common;

namespace Scheduling.Api.Controllers;

/// <summary>Base dos controllers v1: convenções de resposta JSON, Problem Details e ETag.</summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    public const string RoutePrefix = "api/v1";

    protected void SetETag(string version) => Response.Headers.ETag = $"W/\"{version}\"";

    /// <summary>Lê o header If-Match (formato <c>W/"n"</c> ou <c>"n"</c>); ausente → null.</summary>
    protected uint? ReadIfMatch()
    {
        var raw = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        value = value.Trim('"');
        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new PreconditionFailedException(ErrorCodes.AppointmentPreconditionFailed, "Header If-Match inválido.");
    }
}
