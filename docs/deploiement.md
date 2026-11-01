# Déploiement — préprod (gratuite) puis prod

Procédure écrite le 31 octobre 2026 ; préprod passée sur Railway + Vercel le 1er novembre 2026 (partie 2). La variante
« un serveur avec Docker Compose et Caddy » (`deploy/`, partie 3) reste possible pour la prod ou si Railway ne convient pas.

| | Préprod (maintenant) | Prod (plus tard) |
|---|---|---|
| Hébergement | Railway (API + PostgreSQL) et Vercel (interface) | à choisir : VPS payant en Europe (`deploy/`) ou offres payantes |
| Adresse | `…vercel.app` (interface) et `…up.railway.app` (API) | un vrai domaine (`app.mondomaine.com`) |
| Données | **fictives uniquement** (base de démo) | les vraies, après la nuit de bascule (`docs/mise-en-service.md`) |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| Secrets | les siens | **d'autres**, jamais ceux de la préprod |
| Sauvegardes | facultatives | obligatoires, copiées hors du serveur, restauration testée |

## Fichiers de déploiement

- `backend/Dockerfile` (+ `.dockerignore`, `railway.toml`) : l'image de l'API, utilisée par Railway et par `deploy/`.
- `frontend/vercel.json` : construction de l'interface sur Vercel, relais `/api` vers Railway, en-têtes de cache.

### Ce que contient `deploy/` (variante serveur)

- `docker-compose.yml` : trois conteneurs.
  - `db` : PostgreSQL 17, volume `db-data`, joignable par l'API seulement.
  - `api` : l'API .NET (`backend/Dockerfile`) ; migrations appliquées au démarrage, premier Superviseur créé si la base est vide.
  - `web` : Caddy (`web.Dockerfile`) : sert l'interface, obtient le certificat HTTPS tout seul, renvoie `/api` et `/hubs`
    vers l'API (WebSocket compris). Seul conteneur exposé (ports 80 et 443).
- `Caddyfile` : `index.html`, `sw.js`, `registerSW.js`, `manifest.webmanifest` jamais gardés en cache (sinon les mises
  à jour de l'app n'arrivent pas) ; fichiers de `/assets` gardés un an (leur nom change à chaque version).
- `.env.example` : modèle du fichier `.env` (secrets), à copier en `.env` sur le serveur. `.env` n'est jamais versionné.
- `backup.sh` : sauvegarde de la base (`pg_dump`), à lancer chaque nuit par cron.

L'API ne croit l'en-tête `X-Forwarded-For` (adresse du visiteur) que s'il vient de Caddy (réseau Docker `172.30.0.0/24`,
réglage `ReverseProxy__TrustedNetworks`). La limite de connexions par adresse, l'audit et la liste des sessions voient
donc la vraie adresse du visiteur.

## 1. Essai sur ton PC (variante serveur, avec Docker)

Docker Desktop ouvert, dans PowerShell :

```powershell
cd C:\Users\pc\Desktop\hawala\deploy
copy .env.example .env
notepad .env
```

Dans `.env` : `SITE_ADDRESS=:80`, `HTTP_PORT=8088`, `HTTPS_PORT=8443`, et des valeurs pour les secrets (pour un essai
local, n'importe quelles valeurs aléatoires ; voir les commandes `openssl` du modèle, ou Git Bash). Puis :

```powershell
docker compose up -d --build      # 5 à 10 minutes la première fois
docker compose ps                  # les trois conteneurs « running » / « healthy »
docker compose logs -f api         # Ctrl+C pour quitter les journaux
```

Ouvre http://localhost:8088 et connecte-toi avec `SEED_SUPERVISOR_EMAIL` / `SEED_SUPERVISOR_PASSWORD`.
Arrêter : `docker compose down` (les données restent dans les volumes ; `docker compose down -v` efface tout).

## 2. Préprod sur Railway (API + base) et Vercel (interface) — retenue le 1er novembre 2026

Pas de serveur à administrer. Railway construit l'API depuis `backend/Dockerfile` et héberge PostgreSQL ; Vercel
construit l'interface (`frontend/`) et relaie `/api` vers Railway (`frontend/vercel.json`) : pour le navigateur tout
vient de la même adresse, le cookie de session marche. Le relais de Vercel ne transmet pas les WebSocket : le temps réel
se connecte **directement** à Railway (`VITE_HUB_URL`), autorisé par le CORS du hub (`Cors__HubOrigins`).

Coût (à vérifier sur leurs sites) : Railway n'a qu'un crédit d'essai, puis l'offre payante (quelques dollars par mois) ;
Vercel « Hobby » est gratuit mais réservé à un usage non commercial (la prod demandera l'offre Pro ou un autre hébergeur).

### 2.1 Railway : la base et l'API

1. Créer un compte sur https://railway.com (avec GitHub), puis **New Project** → **Deploy from GitHub repo** → le
   dépôt `hawala` (autoriser Railway à le lire).
2. Dans le service créé (l'API) → **Settings** :
   - **Root Directory** : `backend` (Railway y trouve `Dockerfile` et `railway.toml`) ;
   - **Region** : une région d'Europe (la plus proche de Nouakchott et de Dakar) ;
   - **Networking** → **Generate Domain**, et le nommer `hawala-api-preprod` (adresse
     `https://hawala-api-preprod.up.railway.app`). Si ce nom est pris, en choisir un autre et le reporter dans
     `frontend/vercel.json` (ligne `destination`) puis pousser sur GitHub.
3. Dans le projet → **New** → **Database** → **PostgreSQL**. Railway crée le service `Postgres`.
4. Dans le service de l'API → **Variables** (« Raw Editor »). Générer les secrets sur ton PC dans Git Bash
   (`openssl rand -base64 48` pour la clé des jetons, `openssl rand -base64 32` pour la clé de chiffrement) :

```text
ASPNETCORE_ENVIRONMENT=Staging
PORT=8080
ConnectionStrings__Hawala=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
Jwt__SigningKey=<openssl rand -base64 48>
Encryption__Key=<openssl rand -base64 32>
SeedSupervisor__Email=<ton email>
SeedSupervisor__FullName=<ton nom>
SeedSupervisor__TemporaryPassword=<mot de passe temporaire : 8 caractères dont un chiffre>
Cors__HubOrigins=https://<ton-projet>.vercel.app
RateLimits__LoginPerMinute=60
```

   - `${{Postgres.…}}` est remplacé par Railway : laisser tel quel ;
   - `Cors__HubOrigins` : l'adresse Vercel, connue à l'étape 2.2 (y revenir ensuite) ;
   - `RateLimits__LoginPerMinute=60` : derrière les relais de Vercel et de Railway, l'API ne voit pas l'adresse de
     chaque visiteur (limite connue de cette préprod) ; la limite de connexions par adresse est donc relevée pour que
     plusieurs testeurs ne se bloquent pas entre eux. Le verrouillage d'un compte après 5 échecs reste ;
   - copier `Encryption__Key` dans ton gestionnaire de mots de passe.
5. **Deploy**. Dans **Deployments** → **View logs** : migrations appliquées, puis `Now listening on: http://[::]:8080`.
   Vérifier : https://hawala-api-preprod.up.railway.app/api/v1/agencies répond `401` (normal : sans jeton).

### 2.2 Vercel : l'interface

1. Créer un compte sur https://vercel.com (avec GitHub) → **Add New** → **Project** → importer le dépôt `hawala`.
2. **Root Directory** : `frontend` (Vercel lit `vercel.json` : Vite, `npm run build`, dossier `dist`).
3. **Environment Variables** : `VITE_HUB_URL` = `https://hawala-api-preprod.up.railway.app/hubs/live`.
4. **Deploy**. Noter l'adresse donnée (ex. `https://hawala-xxxx.vercel.app`) et la mettre dans `Cors__HubOrigins` côté
   Railway (étape 2.1.4) : Railway redéploie l'API.
5. Ouvrir l'adresse Vercel, se connecter avec `SeedSupervisor__Email` et le mot de passe temporaire, puis le changer.
   Vérifier le temps réel : deux navigateurs, un transfert créé dans l'un apparaît dans l'autre sans recharger (sinon :
   `Cors__HubOrigins` ne correspond pas exactement à l'adresse Vercel, écrite sans `/` final).

### 2.3 Remplir la préprod avec la démo

Depuis ton PC, directement sur l'API (le script s'authentifie par jeton) :

```powershell
cd C:\Users\pc\Desktop\hawala\backend
.\seed-demo.ps1 -ApiUrl https://hawala-api-preprod.up.railway.app
```

Il demande l'email et le mot de passe du Superviseur de la préprod, puis un mot de passe commun pour les comptes de
démo : **les noter**. Compter quelques minutes (il respecte la limite de 300 requêtes par minute).
`seed-demo-history.ps1` ne marche qu'en local : sur la préprod, les courbes se rempliront jour après jour.

### 2.4 Mettre à jour

Chaque `git push` sur `main` redéploie l'API (Railway) et l'interface (Vercel). Les migrations s'appliquent au
démarrage de l'API ; les téléphones voient « Nouvelle version disponible ».

### 2.5 Sauvegardes

Facultatives en préprod. Railway propose des sauvegardes de la base dans le service `Postgres` (onglet **Backups**,
selon l'offre) ; sinon `pg_dump` depuis ton PC avec l'adresse publique de la base (onglet **Connect** du service).

## 3. Variante : un serveur (ex. Oracle Cloud, gratuit) avec Docker Compose et Caddy

### 3.1 Le serveur

1. Créer un compte sur https://www.oracle.com/cloud/free/ (une carte bancaire est demandée pour vérifier l'identité ;
   rien n'est prélevé tant qu'on reste sur l'offre « Always Free »). Choisir une **région d'origine en Europe**
   (Paris, Marseille ou Francfort) : elle ne se change plus.
2. Compute → Instances → **Create instance** :
   - Image : **Ubuntu 24.04** ;
   - Shape : **Ampere (VM.Standard.A1.Flex)**, 2 OCPU et 12 Go de mémoire (dans l'offre gratuite) ;
   - Clé SSH : « Generate a key pair » et **télécharger la clé privée** (à garder précieusement) ;
   - noter l'**adresse IP publique** de l'instance.
   Si Oracle répond « Out of capacity », réessayer plus tard ou dans un autre domaine de disponibilité.
3. Ouvrir les ports 80 et 443 côté Oracle : Instance → Subnet → Security List → **Add Ingress Rules** :
   source `0.0.0.0/0`, TCP, ports `80` puis `443`.

### 3.2 L'adresse gratuite (DuckDNS)

1. Se connecter sur https://www.duckdns.org (compte GitHub ou Google).
2. Créer le sous-domaine, par exemple `hawala-preprod`, et mettre l'**IP publique** de la VM dans « current ip ».
3. Vérifier depuis ton PC : `nslookup hawala-preprod.duckdns.org` doit donner cette IP.

### 3.3 Préparer la VM

Depuis ton PC (PowerShell), avec la clé téléchargée :

```powershell
ssh -i C:\chemin\vers\cle.key ubuntu@IP_PUBLIQUE
```

Puis sur la VM :

```bash
# Les images Ubuntu d'Oracle bloquent tout sauf SSH dans le pare-feu de la machine : ouvrir 80 et 443.
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save

# Docker
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
exit    # puis se reconnecter en SSH pour que le groupe docker soit pris en compte
```

### 3.4 Le code et les secrets

Le dépôt GitHub étant privé, créer un jeton en lecture seule : GitHub → Settings → Developer settings →
Fine-grained tokens → accès au seul dépôt `hawala`, permission « Contents : Read-only ».

```bash
sudo mkdir -p /opt/hawala && sudo chown ubuntu /opt/hawala
git clone https://github.com/houbeid/hawala.git /opt/hawala   # nom : ton compte, mot de passe : le jeton
cd /opt/hawala/deploy
cp .env.example .env
openssl rand -hex 24      # → POSTGRES_PASSWORD
openssl rand -base64 48   # → JWT_SIGNING_KEY
openssl rand -base64 32   # → ENCRYPTION_KEY
nano .env                 # remplir ; SITE_ADDRESS=hawala-preprod.duckdns.org ; ASPNETCORE_ENVIRONMENT=Staging
chmod 600 .env
```

Copier `ENCRYPTION_KEY` et le contenu de `.env` dans ton gestionnaire de mots de passe : **sans cette clé, les
téléphones, NNI et pièces d'identité d'une sauvegarde sont illisibles.**

### 3.5 Démarrer

```bash
cd /opt/hawala/deploy
docker compose up -d --build     # la première construction prend 10 à 20 minutes sur la VM
docker compose ps
docker compose logs -f web       # voir « certificate obtained successfully » pour le HTTPS
```

Ouvrir https://hawala-preprod.duckdns.org, se connecter avec le Superviseur de `.env`, changer son mot de passe.

### 3.6 Remplir avec la démo

Depuis ton PC (l'API est sur Internet, le script passe par elle) :

```powershell
cd C:\Users\pc\Desktop\hawala\backend
.\seed-demo.ps1 -ApiUrl https://hawala-preprod.duckdns.org
```

Le script demande l'email et le mot de passe du Superviseur de la préprod, puis un mot de passe commun pour les comptes
de démo : **les noter**. Il respecte la limite de 300 requêtes par minute (il patiente sur un 429) : compter quelques
minutes. `seed-demo-history.ps1` (photos fictives des 30 jours passés) ne marche qu'en local : sur la préprod, les
courbes se rempliront jour après jour.

### 3.7 Mettre à jour

```bash
cd /opt/hawala && git pull && cd deploy && docker compose up -d --build
```

Les migrations s'appliquent au redémarrage de l'API. Les téléphones voient « Nouvelle version disponible ».

### 3.8 Sauvegardes

```bash
cd /opt/hawala/deploy && chmod +x backup.sh && ./backup.sh
crontab -e     # ajouter : 0 2 * * * cd /opt/hawala/deploy && ./backup.sh >> backups/backup.log 2>&1
```

## 4. Prod (plus tard, payante)

Sur un VPS (partie 3) ou sur des offres payantes de Railway et Vercel, avec :

- un vrai domaine acheté et confié à Cloudflare (DNS), `SITE_ADDRESS=app.mondomaine.com`, `ASPNETCORE_ENVIRONMENT=Production` ;
- de **nouveaux** secrets (`.env` à part) ;
- Cloudflare en mode proxy (nuage orange) : il faudra alors dire à Caddy de croire Cloudflare (`trusted_proxies` avec
  les plages d'adresses de Cloudflare et `client_ip_headers CF-Connecting-IP`), sinon l'adresse vue sera celle de
  Cloudflare. À faire au tour de la prod ;
- les sauvegardes chaque nuit, copiées hors du serveur (stockage objet), et une restauration testée ;
- puis la nuit de bascule (`docs/mise-en-service.md`). **Jamais** de données de démo sur la prod.
