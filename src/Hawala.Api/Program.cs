using Hawala.Api.Configuration;
using Hawala.Api.Endpoints.Identity;
using Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddHawalaAuthentication();

var app = builder.Build();

// Migrations du schéma « identity » puis premier Superviseur si la base est vide.
await app.Services.InitializeIdentityModuleAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Pas de UseHttpsRedirection : en production, Caddy termine le HTTPS devant l'API.
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

await app.RunAsync();