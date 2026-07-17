// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

await app.RunAsync();
