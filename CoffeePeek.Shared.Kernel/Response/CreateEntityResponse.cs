namespace CoffeePeek.Shared.Kernel.Response;

public class CreateEntityResponse : Response
{
    public Guid? EntityId { get; init; }

    /// <summary>
    /// Creates a successful response for entity creation.
    /// </summary>
    public static CreateEntityResponse Success(string message = null, Guid? entityId = null)
    {
        return new CreateEntityResponse
        {
            IsSuccess = true,
            Message = message ?? "Entity created successfully",
            EntityId = entityId
        };
    }

    /// <summary>
    /// Creates an error response for entity creation.
    /// </summary>
    public static CreateEntityResponse Error(string message)
    {
        return new CreateEntityResponse
        {
            IsSuccess = false,
            Message = message,
            Data = null,
        };
    }
}
