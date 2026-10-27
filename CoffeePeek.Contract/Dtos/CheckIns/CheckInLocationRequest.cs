namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record CheckInLocationRequest(double Latitude, double Longitude, double AccuracyMeters,
    DateTimeOffset CapturedAt);
