# Hawala — backend .NET 10 (Nouakchott ↔ Dakar)

PWA de transfert d'argent entre l'agence de Nouakchott (MRO) et celle de Dakar (FCFA / XOF).
Développeur : Hamade (Windows, PowerShell, Docker Desktop, VS Code). **Toujours répondre en français.**

## À lire avant de coder

- Règles métier validées : skill `hawala-mr-sn-domaine` (`.claude/skills/hawala-mr-sn-domaine/SKILL.md`). C'est la source de vérité.
- Méthode de travail : skill `layered-backend-builder` (`.claude/skills/layered-backend-builder/SKILL.md`).
- Architecture complète : @docs/architecture.md

## Méthode de travail (imposée par Hamade)

- Avancer **sous-dossier par sous-dossier** : les fichiers d'un même sous-dossier sont écrits ensemble, puis on s'arrête et on attend son retour.
- Ordre strict : Domain → Application → Infrastructure → Api, puis les tests.
- **Tests à la fin de chaque fonctionnalité** (`dotnet build` puis `dotnet test`). Ne jamais laisser une suite rouge.
- **Prévenir avant de modifier un fichier déjà testé** : quel fichier, pourquoi, quel risque.
- **Ne jamais inventer une règle métier** : poser la question, et noter les cas non tranchés en `// POINT OUVERT : …` dans le code.
- Hamade lance lui-même build, tests et migrations, sauf s'il demande le contraire. Arrêter l'API (Ctrl+C) avant `dotnet test`, sinon les fichiers sont verrouillés.
- Quand une règle est tranchée : mettre à jour le skill `hawala-mr-sn-domaine` et `docs/architecture.md`.

## Sécurité et données

- Données fictives uniquement dans le code, les tests et les seeds (jamais de vrais noms, téléphones, emails ou NNI). Emails de test en `@example.com`.
- Secrets (`Jwt:SigningKey` ≥ 32 octets, `SeedSupervisor:*`) : `dotnet user-secrets` en dev, variables d'environnement en prod. Jamais dans un `appsettings*.json` versionné.
- Le mot de passe temporaire est affiché une fois et n'est jamais journalisé ni stocké en clair.
- `docker-compose.yml` (postgres:17, base/utilisateur `hawala`, mot de passe `hawala_dev`) : dev local seulement.

## Conventions du code

- `Hawala.slnx`, `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, analyseurs), versions NuGet centralisées dans `Directory.Packages.props` (un nouveau csproj ne met **pas** de `Version` sur ses `PackageReference`, sinon NU1008).
- Monolithe modulaire. Chaque module : `X.Contracts` / `X.Domain` / `X.Application` / `X.Infrastructure`. Un module ne voit un autre que par ses `Contracts` (vérifié par `tests/Architecture.Tests`). Domain et Application ne référencent ni EF Core, ni ASP.NET Core, ni Npgsql.
- Un schéma PostgreSQL par module (`identity`, `organization`…), chacun avec sa table `__ef_migrations_history`. Pas de clé étrangère entre schémas.
- Handlers maison (pas de MediatR) : `ICommand`, `ICommand<T>`, `ICommandHandler`, `IQuery<T>`, `IQueryHandler` (`Hawala.SharedKernel.Messaging`).
- `Result` / `Result<T>` / `Error` avec `ErrorType` ; codes stables `Module.Groupe.Raison` (ex. `Identity.Account.EmailTaken`). L'API les traduit en ProblemDetails avec un champ `code` via `ErrorResults.ToProblem()`.
- `IClock` injecté (jamais `DateTime.Now`), IDs UUID v7 via `IdGenerator`. Montants en `decimal` / `numeric(18,2)`.
- Minimal APIs sous `/api/v1`, une route par cas d'usage. Tout endpoint est protégé par défaut ; permission métier via `.RequirePermission(Permissions.X)`. Énumérations renvoyées en texte (`JsonStringEnumConverter`).
- Commande qui touche à la fois les tables ASP.NET Identity et les nôtres : l'enregistrer avec `AddTransactional<TCommand, TResponse, THandler>` (décorateur `TransactionalCommandHandler`).
- Migrations : `dotnet ef migrations add <Nom> --project src/Modules/X/X.Infrastructure --startup-project src/Hawala.Api --context <XDbContext> --output-dir Persistence\Migrations`. Elles s'appliquent au démarrage (Organisation d'abord, puis Identité).
- Tests : xUnit, intégration via `WebApplicationFactory` (environnement `Testing`) + Testcontainers `new PostgreSqlBuilder("postgres:17")`, fixture de collection partagée (`ApiCollection`). Helpers : `TestAccounts`, `ApiClient` (lire les corps avec `ReadJsonAsync`, qui relit le texte).

## État actuel (17 octobre 2026)

Fait et testé sur PostgreSQL (dernier résultat confirmé : **167 réussis, 0 échec**) :

- SharedKernel (Result, Error, IClock, IdGenerator, Messaging, Currency MRO/XOF).
- Identité : connexion email ou téléphone, verrouillage 5 échecs / 15 min, JWT 15 min (`sub`, `sv`, `pwd_change`, `jti`), refresh token rotatif 12 h en cookie `hawala_refresh` (haché, rejeu = révocation de la famille), logout, changement de mot de passe (ferme les autres sessions), `GET /api/v1/auth/me`, contrôle de la SecurityVersion à chaque requête (cache 30 s vidé par intercepteur EF), permissions côté serveur (option B), premier Superviseur créé depuis la configuration.
- Organisation : agences NKC et DKR créées au démarrage, `GET /api/v1/agencies`, `IAgencyDirectory` dans les Contracts.
- Comptes rattachés à une agence (`AgencyId`, interdit pour un Superviseur), migration `AddAccountAgency`.
- `POST /api/v1/users` (permission `users.manage`) : création d'un compte avec mot de passe temporaire généré, 201 + `{ id, temporaryPassword }`. Code écrit (10 fichiers + référence de projet Identity.Application → Organization.Contracts) ; **à vérifier qu'il est bien copié dans le dépôt, puis `dotnet test` à lancer** (tests dans `tests/Identity.IntegrationTests/Users/CreateUserTests.cs`).

## Prochaine étape : gestion des comptes (suite)

1. Bloquer / débloquer un compte (`Block(activeSupervisorCount)` avec `CountActiveSupervisorsForUpdateAsync` dans une transaction ; le blocage coupe les sessions).
2. Changer le rôle (avec l'agence), changer d'agence (`ChangeAgency`), réinitialiser le mot de passe (nouveau mot de passe temporaire généré, `PasswordResetBySupervisor`).
3. Lister les comptes (`GET /api/v1/users`).
4. Changer la couleur d'une agence (permission `agencies.manage`).
5. Tests d'intégration de tout cela.
6. Puis 2FA TOTP du Superviseur.

Ensuite : Money / arrondi / taux, Grand livre, Trésorerie, Tiers, Taux, Transferts, Caisse (budget journalier, contrôle de cohérence 5 %), Bénéfice, Bilan, Notifications, Audit, CI, déploiement. Renommer un jour `Identity.IntegrationTests` en `Hawala.IntegrationTests`.

## Limites connues acceptées

- Fenêtre rare de ≤ 30 s due au cache de session.
- Deux rafraîchissements simultanés peuvent réussir tous les deux : le frontend devra sérialiser les refresh.
- Course sur un index unique (email / téléphone) = 500 au lieu de 409.
- Audit de « qui a créé le compte » : POINT OUVERT, à brancher avec le module Audit.

## Questions ouvertes pour le patron

- Droits par défaut des rôles Agent, Caissier et Consultation.
- Contenu du bouton « + » de l'écran de modification d'un transfert.
- Autres alertes du contrôle de cohérence, en plus du « taux inhabituel ».
