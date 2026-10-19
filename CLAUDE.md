# Hawala — backend .NET 10 (Nouakchott ↔ Dakar)

PWA de transfert d'argent entre l'agence de Nouakchott (MRO) et celle de Dakar (FCFA / XOF).
Développeur : Hamade (Windows, PowerShell, Docker Desktop, VS Code). **Toujours répondre en français.**

## À lire avant de coder

- Règles métier validées : skill `hawala-mr-sn-domaine` (`.claude/skills/hawala-mr-sn-domaine/SKILL.md`). C'est la source de vérité.
- Méthode de travail : skill `layered-backend-builder` (`.claude/skills/layered-backend-builder/SKILL.md`).
- Architecture complète : @docs/architecture.md

## Méthode de travail (imposée par Hamade)

- Avancer **une fonctionnalité complète par tour** (option A, choisie le 19 octobre 2026) : Domain → Application → Infrastructure → Api → tests d'une même fonctionnalité, puis build + tests, et on s'arrête en attendant son retour. Chaque tour se termine au vert. S'arrêter quand même en cours de tour pour : une question métier, une modification risquée d'un fichier déjà testé, une migration à générer (c'est Hamade qui la lance), un problème de conception. Les simples ajouts à des fichiers testés sont signalés dans le compte rendu.
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
- Commande qui touche à la fois les tables ASP.NET Identity et les nôtres, ou qui pose un verrou (`CountActiveSupervisorsForUpdateAsync`) : l'enregistrer avec `AddTransactional<TCommand, TResponse, THandler>`, ou `AddTransactional<TCommand, THandler>` pour une commande sans valeur de retour (décorateurs `TransactionalCommandHandler`).
- L'auteur d'une action (contrôle « soi-même ») est lu dans le JWT par `ClaimsPrincipal.TryGetAccountId` (`Hawala.Api/Configuration`) et passé à la commande en `ActorId`, jamais lu dans le corps de la requête.
- Valeurs d'énumération reçues en texte (rôle, statut) : `EnumNameParser.TryParse<T>` (insensible à la casse, refuse les nombres).
- Migrations : `dotnet ef migrations add <Nom> --project src/Modules/X/X.Infrastructure --startup-project src/Hawala.Api --context <XDbContext> --output-dir Persistence\Migrations`. Elles s'appliquent au démarrage (Organisation d'abord, puis Identité).
- Tests : xUnit, intégration via `WebApplicationFactory` (environnement `Testing`) + Testcontainers `new PostgreSqlBuilder("postgres:17")`, fixture de collection partagée (`ApiCollection`). Helpers : `TestAccounts`, `ApiClient` (lire les corps avec `ReadJsonAsync`, qui relit le texte).

## État actuel (18 octobre 2026)

Fait et testé sur PostgreSQL (dernier résultat confirmé : **443 réussis, 0 échec**) :

- SharedKernel (Result, Error, IClock, IdGenerator, Messaging, Currency MRO/XOF) ; `Money` (FCFA entier, MRO ≤ 2 décimales, saisie MRU ≤ 3 décimales × 10, montant trop précis refusé, MRO + XOF = exception) et `ExchangeRate` (MRO pour 5 000 FCFA, ≤ 2 décimales, `ToTransferMro` arrondi une fois en AwayFromZero).
- Identité : connexion email ou téléphone, verrouillage 5 échecs / 15 min, JWT 15 min (`sub`, `sv`, `pwd_change`, `jti`), refresh token rotatif 12 h en cookie `hawala_refresh` (haché, rejeu = révocation de la famille), logout, changement de mot de passe (ferme les autres sessions), `GET /api/v1/auth/me`, contrôle de la SecurityVersion à chaque requête (cache 30 s vidé par intercepteur EF), permissions côté serveur (option B), premier Superviseur créé depuis la configuration.
- Organisation : agences NKC et DKR créées au démarrage (jamais écrasées), `GET /api/v1/agencies`, `IAgencyDirectory` dans les Contracts, `POST /api/v1/agencies/{id}/change-color` (permission `agencies.manage`, couleur unique par agence, tests dans `tests/Identity.IntegrationTests/Organization/`).
- Comptes rattachés à une agence (`AgencyId`, interdit pour un Superviseur), migration `AddAccountAgency`.
- Gestion des comptes sous `/api/v1/users` (permission `users.manage`), tests dans `tests/Identity.IntegrationTests/Users/` :
  - `POST /users` : création, mot de passe temporaire généré, 201 + `{ id, temporaryPassword }` ;
  - `GET /users` (filtres `agencyId`, `role`, `status`, `search` ; tri par nom ; « verrouillé jusqu'à ») et `GET /users/{id}` ;
  - `POST /users/{id}/block` (coupe les sessions, verrou FOR UPDATE, pas soi-même) et `/unblock` (nouveau mot de passe temporaire, verrouillage levé) ;
  - `POST /users/{id}/change-role` (avec l'agence ; pas soi-même ; dernier Superviseur actif protégé) et `/change-agency` ;
  - `POST /users/{id}/reset-password` (nouveau mot de passe, verrouillage levé, sessions coupées ; pas soi-même ; un compte bloqué reste bloqué) ;
  - `PUT /users/{id}` : nom, email, téléphone (vide = retiré), sessions conservées.
- Exemples de requêtes pour les essais manuels : `src/Hawala.Api/Hawala.Api.http`.
- **Grand livre** (module `Ledger`, schéma `ledger`, migration `AddLedger`) : comptes (caisse, tiers, système par agence), écritures en partie double équilibrées par devise, contre-passation (motif obligatoire, une seule, jamais d'une contre-passation), soldes débit − crédit avec date `asOf`. Porte publique : `ILedger` (Ledger.Contracts), pas de route HTTP. En base : déclencheurs d'immuabilité (UPDATE / DELETE / TRUNCATE refusés, SQLSTATE 23001), équilibre par devise et deux lignes minimum au COMMIT (23514). Tests : `tests/Ledger.Domain.Tests`, `tests/Identity.IntegrationTests/Ledger/`.
- **`Hawala.SharedInfrastructure`** : `SharedConnection` (une connexion PostgreSQL par requête pour les modules d'argent, option A) et le décorateur de transaction commun : une commande qui écrit dans plusieurs schémas s'enregistre avec `AddSharedTransactional<…>`, et chaque `DbContext` d'un module d'argent implémente `ISharedTransactionParticipant`.
- **Périmètre par agence** : `IAgencyScope` (Identity.Contracts), rempli par `SessionValidator` à chaque requête ; filtre global EF Core dans les `DbContext` concernés (`IgnoreQueryFilters` seulement pour une règle qui vaut pour toute l'agence, comme l'unicité d'un nom). Sans session validée : aucune agence visible.
- **Trésorerie** (module `Treasury`, schéma `treasury`, migration `AddTreasury`) : `POST /api/v1/treasury-accounts` (permission `treasury.manage`) crée le compte et son compte au grand livre dans une même transaction ; nom unique par agence (sans tenir compte de la casse) ; devise = celle de l'agence. Tests : `tests/Treasury.Domain.Tests`, `tests/Identity.IntegrationTests/Treasury/`. `GET /api/v1/treasury-accounts` et `GET /api/v1/treasury-accounts/{id}` : périmètre par agence (404 hors agence), solde seulement avec `cash.budget.read`, comptes désactivés masqués sauf `includeInactive=true`. `PUT /api/v1/treasury-accounts/{id}` (renommer), `POST …/{id}/deactivate` (solde nul seulement, ferme aussi le compte au grand livre) et `POST …/{id}/reactivate` (rouvre les deux). Trésorerie complète pour la phase 2.
- **Tiers** (module `Parties`, schéma `parties`, migration `AddParties`) : `POST /api/v1/parties` (permission `parties.create`, donnée aussi au rôle Agent) crée le tiers, commun aux deux agences, et ouvre un compte au grand livre par agence (MRO à NKC, XOF à DKR) dans une même transaction ; téléphone (E.164) et NNI uniques ; nom non unique ; type = compte comptable (Client 41, Supplier 40, Staff 42, Other). Tests : `tests/Parties.Domain.Tests`, `tests/Identity.IntegrationTests/Parties/`. `GET /api/v1/parties` (recherche, type, `balanceSheet` All/In/Out, `balance` All/Red (عليه)/Green (له) sur une agence, archivés ; `count` / `totalCount` ; totaux rouge / vert / net par devise) et `GET /api/v1/parties/{id}` : soldes par agence seulement avec `parties.view_balance` et dans le périmètre. `PUT /api/v1/parties/{id}` (`parties.manage`, Superviseur) : état complet de la fiche, `inBalanceSheet` obligatoire. `PUT /api/v1/parties/{id}/agencies/{agencyId}/opening-balance` (`parties.manage`) : écriture d'ouverture de la différence contre « Soldes d'ouverture » de l'agence, à la date choisie (aujourd'hui par défaut), migration `AddPartyOpeningBalance`. `PUT /api/v1/parties/{id}/agencies/{agencyId}/credit-limit` (`parties.manage`) : positif ou nul, 0 = aucun crédit, null = pas de plafond ; affiché à côté du solde de chaque agence ; appliqué en phase 3. Restent : archiver, relevé.
- Règle de format des téléphones partagée : `PhoneNumbers.TryNormalizeE164` (SharedKernel) ; chaque module garde son type et ses codes d'erreur (`Identity.Phone.*`, `Parties.Phone.*`).
- `EnumNames.TryParse<T>` (SharedKernel) : lecture d'une énumération reçue en texte, pour tous les modules.
- La migration `AddLedger` contient du SQL ajouté à la main (déclencheurs) : si elle est régénérée, le remettre. `dotnet ef migrations remove` a besoin de la base locale (`docker compose up -d`).

## Prochaine étape

1. Phase 2 : Tiers (module Parties). Money, arrondi, Grand livre et Trésorerie : faits.

La CI (GitHub Actions) se fait **en dernier** (décision de Hamade). Reporté aussi : changer le nom d'une agence (le code NKC / DKR ne change jamais).

**Reporté, à discuter avec Hamade avant tout code** : 2FA TOTP du Superviseur (notamment : que peut faire un Superviseur qui ne l'a pas encore activée ?). Ne pas la proposer comme prochaine étape.

Ensuite : Money / arrondi / taux, Grand livre, Trésorerie, Tiers, Taux, Transferts, Caisse (budget journalier, contrôle de cohérence 5 %), Bénéfice, Bilan, Notifications, Audit, CI, déploiement. Renommer un jour `Identity.IntegrationTests` en `Hawala.IntegrationTests`.

## Limites connues acceptées

- Fenêtre rare de ≤ 30 s due au cache de session.
- Deux rafraîchissements simultanés peuvent réussir tous les deux : le frontend devra sérialiser les refresh.
- Course sur un index unique (email / téléphone) = 500 au lieu de 409.
- Audit de « qui a créé / bloqué / modifié le compte » : POINT OUVERT, à brancher avec le module Audit.
- Couleur d'agence unique vérifiée dans le handler, sans index unique en base (course très improbable entre deux Superviseurs) : POINT OUVERT dans `ChangeAgencyColorCommandHandler`.
- Grand livre : soldes recalculés à chaque lecture (pas de cache) ; date d'opération future non refusée (question ouverte) ; création simultanée d'un même compte système = 500 (index unique). POINT OUVERT dans le code.
- Liste des comptes sans pagination, recherche texte faite en mémoire (quelques dizaines de comptes) : POINT OUVERT dans `UserAccountRepository`.
- Le test de concurrence du blocage croisé appelle le handler directement (pas par HTTP) ; il éprouve le verrou de façon probable, pas certaine.

## Questions ouvertes pour le patron

- Droits par défaut des rôles Agent, Caissier et Consultation.
- Autres alertes du contrôle de cohérence, en plus du « taux inhabituel ».
