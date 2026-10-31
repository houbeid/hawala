# Déploiement — préprod (gratuite) puis prod

Procédure écrite le 31 octobre 2026. Les mêmes fichiers (`deploy/`) servent à la préprod et à la prod ; seuls le
fichier `.env` (adresse, secrets) et le serveur changent.

| | Préprod (maintenant) | Prod (plus tard) |
|---|---|---|
| Serveur | VM Oracle Cloud « Always Free » (ARM Ampere) | VPS payant en Europe |
| Adresse | sous-domaine gratuit DuckDNS (`hawala-preprod.duckdns.org`) | un vrai domaine (`app.mondomaine.com`), Cloudflare devant |
| Données | **fictives uniquement** (base de démo) | les vraies, après la nuit de bascule (`docs/mise-en-service.md`) |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| Secrets | les siens | **d'autres**, jamais ceux de la préprod |
| Sauvegardes | facultatives | obligatoires, copiées hors du serveur, restauration testée |

## Ce que contient `deploy/`

- `docker-compose.yml` : trois conteneurs.
  - `db` : PostgreSQL 17, volume `db-data`, joignable par l'API seulement.
  - `api` : l'API .NET (`api.Dockerfile`) ; migrations appliquées au démarrage, premier Superviseur créé si la base est vide.
  - `web` : Caddy (`web.Dockerfile`) : sert l'interface, obtient le certificat HTTPS tout seul, renvoie `/api` et `/hubs`
    vers l'API (WebSocket compris). Seul conteneur exposé (ports 80 et 443).
- `Caddyfile` : `index.html`, `sw.js`, `registerSW.js`, `manifest.webmanifest` jamais gardés en cache (sinon les mises
  à jour de l'app n'arrivent pas) ; fichiers de `/assets` gardés un an (leur nom change à chaque version).
- `.env.example` : modèle du fichier `.env` (secrets), à copier en `.env` sur le serveur. `.env` n'est jamais versionné.
- `backup.sh` : sauvegarde de la base (`pg_dump`), à lancer chaque nuit par cron.

L'API ne croit l'en-tête `X-Forwarded-For` (adresse du visiteur) que s'il vient de Caddy (réseau Docker `172.30.0.0/24`,
réglage `ReverseProxy__TrustedNetworks`). La limite de connexions par adresse, l'audit et la liste des sessions voient
donc la vraie adresse du visiteur.

## 1. Essai sur ton PC (avant le serveur)

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

## 2. Préprod sur Oracle Cloud (gratuit)

### 2.1 Le serveur

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

### 2.2 L'adresse gratuite (DuckDNS)

1. Se connecter sur https://www.duckdns.org (compte GitHub ou Google).
2. Créer le sous-domaine, par exemple `hawala-preprod`, et mettre l'**IP publique** de la VM dans « current ip ».
3. Vérifier depuis ton PC : `nslookup hawala-preprod.duckdns.org` doit donner cette IP.

### 2.3 Préparer la VM

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

### 2.4 Le code et les secrets

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

### 2.5 Démarrer

```bash
cd /opt/hawala/deploy
docker compose up -d --build     # la première construction prend 10 à 20 minutes sur la VM
docker compose ps
docker compose logs -f web       # voir « certificate obtained successfully » pour le HTTPS
```

Ouvrir https://hawala-preprod.duckdns.org, se connecter avec le Superviseur de `.env`, changer son mot de passe.

### 2.6 Remplir la préprod avec la démo

Depuis ton PC (l'API est sur Internet, le script passe par elle) :

```powershell
cd C:\Users\pc\Desktop\hawala\backend
.\seed-demo.ps1 -ApiUrl https://hawala-preprod.duckdns.org
```

Le script demande l'email et le mot de passe du Superviseur de la préprod, puis un mot de passe commun pour les comptes
de démo : **les noter**. Il respecte la limite de 300 requêtes par minute (il patiente sur un 429) : compter quelques
minutes. `seed-demo-history.ps1` (photos fictives des 30 jours passés) ne marche qu'en local : sur la préprod, les
courbes se rempliront jour après jour.

### 2.7 Mettre à jour la préprod

```bash
cd /opt/hawala && git pull && cd deploy && docker compose up -d --build
```

Les migrations s'appliquent au redémarrage de l'API. Les téléphones voient « Nouvelle version disponible ».

### 2.8 Sauvegardes (facultatives en préprod, à essayer quand même)

```bash
cd /opt/hawala/deploy && chmod +x backup.sh && ./backup.sh
crontab -e     # ajouter : 0 2 * * * cd /opt/hawala/deploy && ./backup.sh >> backups/backup.log 2>&1
```

## 3. Prod (plus tard, serveur payant)

Mêmes étapes que la préprod sur le VPS, avec :

- un vrai domaine acheté et confié à Cloudflare (DNS), `SITE_ADDRESS=app.mondomaine.com`, `ASPNETCORE_ENVIRONMENT=Production` ;
- de **nouveaux** secrets (`.env` à part) ;
- Cloudflare en mode proxy (nuage orange) : il faudra alors dire à Caddy de croire Cloudflare (`trusted_proxies` avec
  les plages d'adresses de Cloudflare et `client_ip_headers CF-Connecting-IP`), sinon l'adresse vue sera celle de
  Cloudflare. À faire au tour de la prod ;
- les sauvegardes chaque nuit, copiées hors du serveur (stockage objet), et une restauration testée ;
- puis la nuit de bascule (`docs/mise-en-service.md`). **Jamais** de données de démo sur la prod.
