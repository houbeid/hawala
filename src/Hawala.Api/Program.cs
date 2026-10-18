using System.Text.Json.Serialization;
using Hawala.Api.Configuration;
using Hawala.Api.Endpoints.Identity;
using Hawala.Api.Endpoints.Organization;
using Identity.Infrastructure;
using Organization.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Énumérations en texte dans toutes les réponses : « MRO » plutôt que 0.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOrganizationModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddHawalaAuthentication();

var app = builder.Build();

// Organisation d'abord : les comptes seront rattachés aux agences.
await app.Services.InitializeOrganizationModuleAsync();

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
app.MapUserEndpoints();
app.MapAgencyEndpoints();

await app.RunAsync();
