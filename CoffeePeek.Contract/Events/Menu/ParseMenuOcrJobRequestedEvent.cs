namespace CoffeePeek.Contract.Events.Menu;

/// <summary>A durable request to process one published shop's persisted OCR job.</summary>
public sealed record ParseMenuOcrJobRequestedEvent(Guid JobId, Guid? RequestedByUserId);
