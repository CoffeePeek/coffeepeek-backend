using CoffeePeek.ShopsService;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);

builder.AddApplication();

var app = builder.Build();

app.UseApplication();

return await app.RunJasperFxCommands(args);
