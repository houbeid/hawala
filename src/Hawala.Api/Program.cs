using System.Text.Json.Serialization;
using Hawala.Api.Configuration;
using Audit.Infrastructure;
using Balance.Infrastructure;
using Cash.Infrastructure;
using Hawala.Api.Endpoints.Audit;
using Hawala.Api.Endpoints.Balance;
using Hawala.Api.Endpoints.Cash;
using Hawala.Api.Endpoints.Identity;
using Hawala.Api.Endpoints.Organization;
using Hawala.Api.Endpoints.Profit;
using Hawala.Api.Endpoints.Parties;
using Hawala.Api.Endpoints.Rates;
using Hawala.Api.Endpoints.Transfers;
using Hawala.Api.Endpoints.Treasury;
using Hawala.Api.Realtime;
using Identity.Infrastructure;
using Ledger.Infrastructure;
using Organization.Infrastructure;
using Parties.Infrastructure;
using Profit.Infrastructure;
using Rates.Infrastructure;
using Transfers.Application.Abstractions;
using Transfers.Infrastructure;
using Treasury.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Énumérations en texte dans toutes les réponses : « MRO » plutôt que 0.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOrganizationModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddLedgerModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddTreasuryModule(builder.Configuration);
builder.Services.AddPartiesModule(builder.Configuration);
builder.Services.AddRatesModule(builder.Configuration);
builder.Services.AddTransfersModule(builder.Configuration);
builder.Services.AddCashModule(builder.Configuration);
builder.Services.AddProfitModule(builder.Configuration);
builder.Services.AddBalanceModule(builder.Configuration);
builder.Services.AddHawalaAuthentication();
builder.Services.AddHawalaRateLimiting(builder.Configuration);

// Temps réel : un hub, des groupes par agence et pour les Superviseurs ; les Transferts publient après validation.
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<ITransferChangePublisher, SignalRTransferChangePublisher>();
builder.Services.AddScoped<SessionNotifications>();

var app = builder.Build();

// Organisation d'abord : les comptes seront rattachés aux agences.
await app.Services.InitializeOrganizationModuleAsync();

// Migrations du schéma « identity » puis premier Superviseur si la base est vide.
await app.Services.InitializeIdentityModuleAsync();

// Grand livre : tables, index et déclencheurs (immuabilité, équilibre par devise au COMMIT).
await app.Services.InitializeLedgerModuleAsync();

// Journal d'audit : table en insertion seule (déclencheurs), écrite dans la transaction de chaque commande tracée.
await app.Services.InitializeAuditModuleAsync();

// Trésorerie : après le grand livre, qu'elle appelle pour ouvrir le compte de chaque caisse.
await app.Services.InitializeTreasuryModuleAsync();

// Tiers : après le grand livre, qu'ils appellent pour ouvrir un compte par agence.
await app.Services.InitializePartiesModuleAsync();

// Taux du jour : lus par les transferts.
await app.Services.InitializeRatesModuleAsync();

// Transferts : en dernier, ils écrivent au grand livre et lisent la trésorerie et les taux.
await app.Services.InitializeTransfersModuleAsync();

// Caisse : mouvements manuels, écrits au grand livre.
await app.Services.InitializeCashModuleAsync();

// Bénéfice : photos du bénéfice du jour.
await app.Services.InitializeProfitModuleAsync();

// Bilan général : capital initial et photos du bilan.
await app.Services.InitializeBalanceModuleAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Pas de UseHttpsRedirection : en production, Caddy termine le HTTPS devant l'API.
app.UseAuthentication();

// Après l'authentification : la limite générale se compte par compte connecté (phase 6).
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAgencyEndpoints();
app.MapTreasuryEndpoints();
app.MapPartyEndpoints();
app.MapAccountPartyScopeEndpoints();
app.MapRateEndpoints();
app.MapTransferEndpoints();
app.MapCashEndpoints();
app.MapProfitEndpoints();
app.MapBalanceEndpoints();
app.MapAuditEndpoints();
app.MapHub<LiveHub>(LiveHub.Path);

await app.RunAsync();
