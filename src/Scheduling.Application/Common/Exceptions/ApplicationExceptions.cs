namespace Scheduling.Application.Common.Exceptions;

/// <summary>Base das exceções de aplicação; cada subtipo corresponde a um status HTTP (research R-07).</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404 — recurso inexistente ou pertencente a terceiros.</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message);

/// <summary>409 — conflito de estado (unicidade, sobreposição, concorrência).</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>403 — perfil sem permissão.</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message);

/// <summary>401 — não autenticado ou credenciais inválidas.</summary>
public sealed class UnauthenticatedException(string code, string message) : AppException(code, message);

/// <summary>412 — versão informada em If-Match não corresponde à atual.</summary>
public sealed class PreconditionFailedException(string code, string message) : AppException(code, message);

/// <summary>422 — regra de negócio violada.</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message);
