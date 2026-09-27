using System.Net;

namespace CoffeePeek.Shared.Kernel.Exceptions;

public class ServiceUnavailableException : BaseException
{
    public ServiceUnavailableException(string message, string? errorCode = null)
        : base(message, errorCode, (int)HttpStatusCode.ServiceUnavailable)
    {
    }

    public ServiceUnavailableException(string message, Exception innerException, string? errorCode = null)
        : base(message, innerException, errorCode, (int)HttpStatusCode.ServiceUnavailable)
    {
    }
}
