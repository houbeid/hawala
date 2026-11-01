# Déploiement — production sur Render

Procédure écrite le 31 octobre 2026, revue le 1er novembre 2026 : **la base hébergée est la production** (décision
du 1er novembre 2026 : on y saisit les vraies données, il n'y a pas de préprod séparée). L'API, la base et l'interface
sont sur Render (partie 2 ; Railway, prévu d'abord, avait fermé ses inscriptions). La variante « un serveur avec Docker
Compose » (`deploy/`, partie 3) reste possible si Render ne convient plus.

**Règles de la production :**

- **Jamais de données de démonstration** : le grand livre ne s'efface pas, des transferts fictifs fausseraient pour
  toujours la caisse et le bilan. `seed-demo.ps1` refuse toute API qui n'est pas sur ton PC. Les essais se font en local.
- **Sauvegarde chaque nuit, hors de Render** (partie 2.5), restauration essayée au moins une fois.
- **Secrets en lieu sûr** (gestionnaire de mots de passe), jamais dans le dépôt ni dans un message : surtout
  `Encryption__Key`, sans laquelle téléphones, NNI et pièces d'identité d'une sauvegarde sont illisibles.
- **Un compte et un mot de passe par personne**, jamais partagés.
- Mise en service des vraies données : `docs/mise-en-service.md` (date de démarrage, soldes, import des tiers, reprise
  des non livrés, capital).

## Fichiers de déploiement

- `backend/Dockerfile` (+ `.dockerignore`) : l'image de l'API.
- `frontend/Dockerfile` (+ `.dockerignore`, `Caddyfile`) : l'interface servie par Caddy, qui renvoie
  `/api` et `/hubs` vers l'API (WebSocket compris) : une seule adresse pour le navigateur, le cookie de session et le
  temps réel marchent sans réglage. `index.html`, `sw.js`, `registerSW.js`, `manifest.webmanifest` jamais gardés en
  cache (sinon les mises à jour de l'app n'arrivent pas) ; `/assets` gardé un an (noms versionnés).
- `deploy/backup-remote.ps1` : sauvegarde de la base hébergée sur ton PC (Docker), relue avant d'être gardée.

### Ce que contient `deploy/` (variante serveur)

- `docker-compose.yml` : trois conteneurs.
  - `db` : PostgreSQL 17, volume `db-data`, joignable par l'API seulement.
  - `api` : l'API .NET (`backend/Dockerfile`) ; migrations appliquées au démarrage, premier Superviseur créé si la base est vide.
  - `web` : Caddy (`frontend/Dockerfile`) : sert l'interface, obtient le certificat HTTPS tout seul, renvoie `/api` et
    `/hubs` vers l'API. Seul conteneur exposé (ports 80 et 443).
- `.env.example` : modèle du fichier `.env` (secrets), à copier en `.env` sur le serveur. `.env` n'est jamais versionné.
- `backup.sh` : sauvegarde de la base (`pg_dump`) sur le serveur, à lancer chaque nuit par cron.

Dans cette variante, l'API ne croit l'en-tête `X-Forwarded-For` (adresse du visiteur) que s'il vient de Caddy (réseau
Docker `172.30.0.0/24`, réglage `ReverseProxy__TrustedNetworks`).

## 1. Essai sur ton PC (avec Docker)

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

## 2. Production sur Render (API, base et interface) — décision du 1er novembre 2026

Railway ayant fermé ses inscriptions le jour de la mise en ligne, la production va sur Render, avec les mêmes images.
Trois éléments dans **la même région, Frankfurt (EU Central)** :

| Élément | Type Render | Dossier | Offre |
| --- | --- | --- | --- |
| `hawala-db` | PostgreSQL 17 | — | Basic-256mb |
| `hawala-api` | **Private Service** (aucune adresse publique) | `backend` | Starter |
| `elveth` | **Web Service** (l'adresse donnée aux agents) | `frontend` | Starter |

Seul `elveth` est public ; Caddy y parle à l'API par le réseau privé de Render. Ne pas prendre les offres gratuites :
la base gratuite est **supprimée au bout de 30 jours**, et un service gratuit s'endort après 15 minutes sans visite
(une minute d'attente au réveil, et les Private Services n'existent pas en gratuit). Coût : environ 6 + 7 + 7 = 20 $
par mois (vérifier sur https://render.com/pricing), facturé à prix fixe par service.

### 2.1 Le compte et la base

1. https://render.com → **Get Started** (avec GitHub), puis **Billing** : ajouter une carte.
2. **+ New** → **Postgres** :
   - Name `hawala-db`, Database `hawala`, User `hawala` ;
   - Region **Frankfurt (EU Central)** ;
   - PostgreSQL Version **17** ;
   - Plan **Basic-256mb** (pas Free) → **Create Database**.
3. Quand elle est « Available », page de la base → **Connect** / **Info** : garder sous les yeux le **Hostname**
   interne (de la forme `dpg-…-a`, sans domaine), le Port (5432), la Database, le Username et le Password.

### 2.2 Le service `hawala-api`

1. **+ New** → **Private Service** → **Git Provider** → dépôt `hawala` (autoriser Render à le lire).
2. Réglages :
   - Name `hawala-api`, Region **Frankfurt** (la même que la base), Branch `main` ;
   - Root Directory `backend`, Language **Docker** (Dockerfile `./Dockerfile`) ;
   - Instance Type **Starter**.
3. **Environment Variables** (« Add from .env » permet de tout coller d'un coup). Générer les secrets sur ton PC dans
   Git Bash (`openssl rand -base64 48` pour la clé des jetons, `openssl rand -base64 32` pour la clé de chiffrement) et
   les copier d'abord dans ton gestionnaire de mots de passe :

```text
ASPNETCORE_ENVIRONMENT=Production
PORT=8080
ConnectionStrings__Hawala=Host=<Hostname interne>;Port=5432;Database=hawala;Username=hawala;Password=<Password de la base>
Jwt__SigningKey=<openssl rand -base64 48>
Encryption__Key=<openssl rand -base64 32>
SeedSupervisor__Email=<email du premier Superviseur>
SeedSupervisor__FullName=<son nom>
SeedSupervisor__TemporaryPassword=<mot de passe temporaire : 8 caractères dont un chiffre>
RateLimits__LoginPerMinute=60
```

   - `ConnectionStrings__Hawala` s'écrit ainsi (pas l'adresse `postgresql://…`, que l'API ne lit pas) ;
   - `RateLimits__LoginPerMinute=60` : derrière les relais de Render et de Caddy, l'API ne voit pas encore l'adresse
     de chaque visiteur (POINT OUVERT, à vérifier dans « Mes appareils » après la mise en ligne) ; la limite par adresse
     est relevée pour que les agents ne se bloquent pas entre eux. Le verrouillage d'un compte après 5 échecs reste.
4. **Deploy Private Service**. Onglet **Logs** : migrations appliquées puis `Now listening on: http://[::]:8080`.
5. En haut de la page du service, noter son **adresse interne** (de la forme `hawala-api:8080`, parfois avec un
   suffixe) : c'est l'`API_UPSTREAM` de 2.3.

### 2.3 Le service `elveth` (l'interface)

1. **+ New** → **Web Service** → dépôt `hawala` de nouveau.
2. Réglages :
   - Name `elveth` (l'adresse sera `https://elveth.onrender.com`, avec un suffixe si le nom est pris) ;
   - Region **Frankfurt**, Branch `main`, Root Directory `frontend`, Language **Docker** ;
   - Instance Type **Starter** ;
   - Advanced → Health Check Path `/`.
3. **Environment Variables** :

```text
PORT=8080
SITE_ADDRESS=:8080
API_UPSTREAM=http://<adresse interne de hawala-api, ex. hawala-api:8080>
ACME_EMAIL=<ton email>
```

4. **Deploy Web Service**, puis ouvrir l'adresse affichée en haut de la page, se connecter avec
   `SeedSupervisor__Email` et le mot de passe temporaire, le changer pour un mot de passe solide. Vérifier :
   - `https://<adresse>/api/v1/agencies` répond `401` (normal : sans jeton) ; une erreur `502` veut dire
     qu'`API_UPSTREAM` est faux ou que `hawala-api` ne tourne pas ;
   - le temps réel : deux navigateurs, un taux changé dans l'un apparaît dans l'autre sans recharger ;
   - « Mes appareils » : noter l'adresse affichée (adresse réelle du téléphone ou adresse de Render ? c'est le POINT
     OUVERT de 2.2).
5. Plus tard, un nom de domaine à toi (`app.mondomaine.com`) s'ajoute dans **Settings** → **Custom Domains**.

### 2.4 Mise en service

Suivre `docs/mise-en-service.md` (soir de J) : sauvegarde avant import (2.5), comptes des agents, comptes de trésorerie
et leurs soldes de départ, date de démarrage, import des tiers, reprise des non livrés, capital, photos du jour.

### 2.5 Sauvegardes chaque nuit (obligatoire)

Render garde aussi des sauvegardes de la base payante (voir l'onglet **Recovery** de la base), mais chez Render
seulement : la copie de chaque nuit sur ton PC reste obligatoire. Elle tourne sur ton PC (Docker Desktop ouvert).

1. Render → `hawala-db` → **Connect** → **External** : copier l'**External Database URL** `postgresql://…` (elle
   contient le mot de passe : ne la donner à personne).
2. L'enregistrer pour ton compte Windows seulement (PowerShell) :

```powershell
[Environment]::SetEnvironmentVariable('HAWALA_BACKUP_DATABASE_URL', '<External Database URL>', 'User')
```

3. Essayer (nouvelle fenêtre PowerShell) :

```powershell
cd C:\Users\pc\Desktop\hawala\deploy
.\backup-remote.ps1
```

   La copie arrive dans `Documents\hawala-sauvegardes` (`hawala-aaaammjj-hhmm.dump`) ; `sauvegardes.log` dit si elle a
   été relue sans erreur. Les copies de plus de 30 jours sont effacées.
4. Chaque nuit : Planificateur de tâches Windows → **Créer une tâche de base** → « Sauvegarde Hawala », tous les jours à
   23 h (heure de fin de journée), action **Démarrer un programme** :
   - programme : `powershell.exe`
   - arguments : `-NoProfile -ExecutionPolicy Bypass -File "C:\Users\pc\Desktop\hawala\deploy\backup-remote.ps1"`
   - le PC doit être allumé et Docker ouvert ; sinon, lancer le script à la main le matin.
5. Copier aussi `Documents\hawala-sauvegardes` ailleurs (OneDrive, Google Drive, disque externe) : une sauvegarde sur un
   seul PC peut disparaître avec lui.

**Restaurer** (seulement en cas de perte ; arrêter d'abord `hawala-api` : **Settings** → **Suspend Service**) :

```powershell
$dossier = "$env:USERPROFILE\Documents\hawala-sauvegardes"
docker run --rm -v "${dossier}:/backup" postgres:17 pg_restore --clean --if-exists --no-owner --dbname="<External Database URL>" /backup/<fichier>.dump
```

puis **Resume Service**. Essayé le 1er novembre 2026 sur une copie : mêmes transferts et mêmes écritures après
restauration, y compris par-dessus une base déjà remplie.

### 2.6 Mettre à jour l'application

Chaque `git push` sur `main` redéploie `hawala-api` et `elveth` (Auto-Deploy « On Commit », réglage par défaut). Les
migrations s'appliquent au démarrage de l'API ; les téléphones voient « Nouvelle version disponible ». Faire une
sauvegarde (2.5) avant une mise à jour qui contient une migration.

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

### 3.6 Données de démonstration : jamais en production

`seed-demo.ps1` refuse toute API qui n'est pas sur ton PC. Les démonstrations se font en local (base `hawala_demo`).

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

## 4. Plus tard

- **Un nom de domaine à toi** (environ 10 € par an), ajouté au service `elveth` (**Custom Domains**) ; les agents gardent
  la même application (réinstaller la PWA depuis la nouvelle adresse).
- **Adresse réelle des visiteurs** derrière Render (POINT OUVERT de 2.2) : selon ce qu'affiche « Mes appareils »,
  régler `trusted_proxies` dans le Caddyfile et `ReverseProxy__TrustedNetworks` dans l'API, puis remettre
  `RateLimits__LoginPerMinute` à 10.
- Si Render ne convient plus : la variante serveur (partie 3) avec une sauvegarde restaurée (2.5) ; derrière
  Cloudflare, dire à Caddy de le croire (`trusted_proxies` avec ses plages d'adresses et `client_ip_headers
  CF-Connecting-IP`).
