---
name: "hawala-mr-sn-domaine"
description: "Règles métier, décisions validées et conventions de l'app Hawala Mauritanie–Sénégal. À charger avant tout code, conception ou question sur ce projet."
---

# App Hawala Mauritanie–Sénégal — référentiel du projet

Nouvelle app web (PWA) de transfert d'argent entre Nouakchott et Dakar, inspirée d'une app existante (alhawala, v1.0.57) avec de nombreux changements fonctionnels et UI/UX. Ce skill est la source de vérité sur les règles validées. Le document d'architecture complet est dans le dépôt : `docs/architecture.md` (copie de « Architecture — App Hawala MR–SN », Claude Docs). L'état d'avancement et la prochaine étape sont dans `CLAUDE.md` à la racine.

À utiliser avec `layered-backend-builder` (backend) et `react-frontend-builder` (frontend) : ce skill dit QUOI construire, eux disent COMMENT avancer (fichier par fichier, tests avant de continuer).

## Règles de travail

- Ne jamais inventer une règle métier. Si un cas n'est pas couvert ici, poser la question à l'utilisateur et lister la question dans « Questions ouvertes ».
- Une règle marquée **validée** ne change pas sans accord explicite de l'utilisateur.
- Ne jamais utiliser de vrais noms, téléphones, emails ou NNI de clients dans le code, les tests ou les seeds : données fictives uniquement.
- Répondre en français.
- Secrets (`Jwt:SigningKey`, `SeedSupervisor:*`) uniquement dans les user-secrets (dev) ou des variables d'environnement (prod), jamais dans un appsettings versionné. Les identifiants du docker-compose sont pour le dev local seulement.
- Un mot de passe temporaire n'est jamais journalisé ni stocké en clair.

## Périmètre (validé)

- Un seul corridor : Mauritanie ↔ Sénégal. Deux agences : Nouakchott et Dakar.
- Deux devises seulement : ouguiya et FCFA (XOF).
- **L'ouguiya est stockée et affichée en MRO** (ancienne ouguiya, 1 MRU = 10 MRO), comme l'app actuelle. « MRO » n'est plus un code ISO : c'est un code interne ; ne pas déléguer le formatage à une lib ISO stricte.
- **Tous les affichages sont en MRO**, banques et wallets compris (correction du 19 octobre 2026). Le MRU ne sert qu'à la **saisie** : un montant entré en MRU (ex. lu sur un relevé Bankily) est converti en MRO × 10 ; il n'est jamais affiché en MRU.
- Montants en `decimal` / `numeric(18,2)`, jamais `float`/`double`.

## Arrondi (validé)

1. Le FCFA est toujours en nombres entiers ; les taux gardent 2 décimales (3 604,77).
2. Le montant MRO d'un transfert (FCFA × taux / 5000) est arrondi à l'unité la plus proche, 0,5 vers le haut : `MidpointRounding.AwayFromZero`, jamais l'arrondi par défaut de .NET (vers le pair).
3. On arrondit une seule fois, à la création du transfert ; le montant arrondi est enregistré et jamais recalculé.
4. Bénéfice et bilan : calcul en pleine précision sur les montants enregistrés, arrondi seulement du total affiché.
5. MRU ↔ MRO : ×10 exact.
6. **Un taux saisi avec plus de 2 décimales est refusé** (jamais arrondi) : le Superviseur voit exactement le taux appliqué.
7. **Les montants MRO hors transferts** (caisse, banque, tiers) **peuvent avoir des décimales.**
8. **La saisie en MRU avec décimales est acceptée** (ex. 12,5 MRU d'un relevé Bankily = 125 MRO).
9. **Au plus 2 décimales en MRO, donc au plus 3 décimales en MRU** (12,345 MRU = 123,45 MRO) ; au-delà, **refusé**, jamais arrondi.

Si le test de référence (536 893 MRO) montre un écart, on ajuste le point 4, jamais la règle des transferts.

## Organisation et règlement entre agences (validé)

- **Un seul patron, deux caisses** : une caisse à Nouakchott (MRO), une caisse à Dakar (FCFA).
- Transfert Nouakchott → Dakar : l'ouguiya entre dans la caisse de Nouakchott, le FCFA sort de la caisse de Dakar (et inversement). Pas de compte de dette entre agences : les deux caisses appartiennent au même patron et le bilan les additionne. Le passage de devise se fait par le compte « Position de change ».
- Le remplissage des caisses (achat de FCFA, banque, transport…) est **hors périmètre** de l'app. Les agents enregistrent seulement :
  - « Approvisionnement de caisse » (montant seul, sans source) ;
  - « Retrait du patron » (montant seul).
  Les deux passent par un compte « Apports du patron » rattaché au capital : ni gain ni perte.
- **Caisse insuffisante : la livraison est autorisée.** La caisse peut passer en négatif ; avertissement à l'agent, sans blocage.

## Périmètre par agence (validé)

Mêmes règles pour Nouakchott et Dakar :

- Un compte agent appartient à **une seule agence** ; le choix de l'agence est verrouillé dans l'interface.
- Une agence voit **les transferts qu'elle a envoyés et ceux qu'elle a reçus** (à livrer), sa propre caisse et ses propres données, jamais celles de l'autre agence.
- Une agence voit **ses montants (acheté, donné au client) et le prix de vente**. Elle ne voit **ni le prix d'achat, ni la commission**, ni les quatre cartes de l'écran des transferts : réservés au Superviseur.
- Le **Superviseur voit les deux agences** (gestion des comptes, taux, commission, bilan total).
- Contrôle côté serveur (filtre global EF Core sur l'agence, champs masqués dans les réponses de l'API), jamais seulement dans l'interface.

## Taux et commission (validé)

- Deux taux du jour, au format « 5 000 FCFA = ? MRO », enregistrés **jour par jour** :
  - **prix d'achat** = taux du marché (T), ce que coûtent les FCFA ;
  - **prix de vente** = taux appliqué au client.
- **Seul le Superviseur fixe ou modifie un taux.** Le prix de vente du jour s'applique automatiquement à chaque nouveau transfert ; le Superviseur peut le changer pour un transfert précis. Les agents des deux agences ne touchent à aucun taux ; le serveur refuse (permissions `rates.set_daily`, `transfers.edit_rate`).
- **Commission = écart entre prix de vente et prix d'achat × montant.** Exemple : 5 000 000 FCFA vendus à 3 650 (le client paie 3 650 000 MRO), achetés à 3 600 (3 600 000 MRO) → 50 000 MRO. C'est la formule du bénéfice ci-dessous.
- Chaque transfert garde ses taux (historisés).
- **Un seul taux du jour pour les deux agences** (un seul corridor), validé le 20 octobre 2026.
- **Sans taux fixé pour la date de l'opération, pas de transfert** : l'agent voit « taux du jour non fixé ».
- **Le taux d'un jour peut être changé à tout moment** par le Superviseur ; les transferts déjà créés gardent leurs taux, seuls les nouveaux prennent le nouveau.
- **Le prix d'achat n'est visible qu'avec la permission `profit.view`** (Superviseur) ; les agents ne voient que le prix de vente.
- **Alerte « taux de change inhabituel »** si le taux d'un transfert s'écarte de **plus de 5 %** du taux du jour (avec 3 600 : hors de 3 420 à 3 780). Alerte seulement, sans blocage.

## Comptes et permissions (validé)

- Un compte **Superviseur** gère les autres comptes : il attribue la branche (Nouakchott ou Dakar, donc le pays) et sa couleur, crée un compte, active ou bloque un compte, accorde les permissions, et peut limiter un compte à une liste de tiers autorisés. Contrôle par la permission `users.manage`, pas par le nom du rôle.
- **Plusieurs Superviseurs possibles.** Un Superviseur peut bloquer un autre Superviseur et peut se donner des permissions à lui-même (tracé dans l'audit).
- **Le dernier Superviseur actif ne peut être ni bloqué ni rétrogradé** (comptage avec verrou `SELECT … FOR UPDATE`).
- **Un Superviseur ne peut ni bloquer, ni changer le rôle, ni réinitialiser le mot de passe de son propre compte** (il change son mot de passe par `/auth/change-password`). Pas de motif obligatoire pour bloquer un compte.
- **Réinitialiser le mot de passe** (par le Superviseur) : nouveau mot de passe temporaire généré, **lève le verrouillage** (5 échecs / 15 min) et **coupe toutes les sessions** du compte. Possible aussi sur un compte bloqué et sur un autre Superviseur.
- **Débloquer un compte : l'ancien mot de passe n'est plus valable** ; le déblocage génère un nouveau mot de passe temporaire (affiché une fois, changement obligatoire à la connexion) et lève le verrouillage.
- **Le Superviseur peut modifier le nom, l'email et le téléphone d'un compte, y compris le sien** (mêmes règles d'unicité qu'à la création). Le téléphone peut être retiré (champ vide). Ce changement ne coupe pas les sessions.
- **Liste des comptes** : nom, email, téléphone, rôle, agence (code, nom, couleur), statut, « mot de passe à changer », « verrouillé jusqu'à ». Filtres agence, rôle, statut + recherche texte (nom, email, téléphone) ; comptes bloqués affichés par défaut ; tri par nom ; pas de pagination pour l'instant. Dernière connexion : reportée à la phase 6 (avec les appareils connectés).
- Le premier Superviseur est créé au démarrage depuis la configuration (user-secrets en dev, variables d'environnement en prod), seulement si aucun Superviseur n'existe.
- App actuelle : rôles Administration / Membre + permissions Voir / Ajouter / Modifier / Supprimer par élément (tiers, opérations financières, transferts…) + « Lire le budget de la caisse » et « Enregistrer le budget de la caisse ». Nouvelle app : modèles de rôles (Superviseur, Agent, Caissier, Consultation) + ajouts / retraits par compte, permissions métier (livrer, annuler, modifier le taux, activer le crédit, voir bénéfice, voir bilan, exporter, `cash.budget.read`, `cash.budget.save`), plafonds par compte, 2FA obligatoire pour le Superviseur, audit lisible.
- Contrôle côté serveur en 3 couches : permission → périmètre (agence, tiers autorisés, via filtre global EF Core) → plafond.

### Implémenté (gestion des comptes)

- Agences en base (module Organisation) : **NKC** Nouakchott (Mauritanie, MRO, #1E8E3E, Africa/Nouakchott) et **DKR** Dakar (Sénégal, XOF, #1565C0, Africa/Dakar), créées au démarrage si absentes, jamais écrasées. Code = 3 lettres majuscules ; la devise découle du pays ; couleur #RRGGBB.
- `UserAccount.AgencyId` : obligatoire pour Agent / Caissier / Consultation, **interdit pour un Superviseur** (il voit les deux agences). Pas de clé étrangère entre schémas : l'existence de l'agence est vérifiée par `IAgencyDirectory` (Organization.Contracts).
- **Couleur d'agence** : le Superviseur la change (permission `agencies.manage`). **Chaque agence a sa propre couleur** : deux agences ne peuvent pas avoir la même (elle sert à distinguer Nouakchott de Dakar d'un coup d'œil). Changer le **nom** d'une agence : reporté, pas maintenant. Le code (NKC, DKR) ne change jamais.
- **Le Superviseur peut déplacer un compte d'une agence à l'autre** (`ChangeAgency`, SecurityVersion incrémentée).
- **Le mot de passe temporaire est généré par l'app** (format `xxx-xxx-xxx`, sans 0/O/1/l/I, au moins un chiffre), renvoyé une seule fois dans la réponse de création.
- Permissions définies dans `Identity.Contracts.Permissions` (une permission n'y entre qu'avec son premier endpoint). Endpoints protégés par `.RequirePermission(Permissions.X)` ; le contrôle passe par `IPermissionChecker`, lu côté serveur (option B).

### Droits par défaut des rôles (validés le 20 octobre 2026)

Chaque rôle ne voit que son agence (sauf le Superviseur). Ajustables compte par compte en phase 6.

| Permission | Superviseur | Agent | Caissier | Consultation |
|---|---|---|---|---|
| Créer un transfert | ✔ | ✔ | ✘ | ✘ |
| Livrer un transfert | ✔ | ✔ | ✔ | ✘ |
| Annuler un transfert (motif obligatoire) | ✔ | ✘ | ✘ | ✘ |
| Activer le crédit (dans le plafond) | ✔ | ✔ | ✘ | ✘ |
| Modifier le taux d'un transfert ; fixer les taux du jour | ✔ | ✘ | ✘ | ✘ |
| Créer un tiers (`parties.create`) | ✔ | ✔ | ✘ | ✘ |
| Modifier un tiers (`parties.manage`) | ✔ | ✘ | ✘ | ✘ |
| Voir les soldes des tiers de son agence (`parties.view_balance`) | ✔ | ✔ | ✘ | ✔ |
| Encaissement / décaissement manuel (phase 4) | ✔ | ✔ | ✔ | ✘ |
| Lire le budget de la caisse (`cash.budget.read`, dont les soldes des comptes de trésorerie) | ✔ | ✔ | ✔ | ✔ |
| Enregistrer le budget de la caisse | ✔ | ✘ | ✔ | ✘ |
| Bénéfice, bilan, les quatre cartes | ✔ | ✘ | ✘ | ✘ |
| Exporter | ✔ | ✘ | ✘ | ✔ |
| Gérer comptes, agences, caisses | ✔ | ✘ | ✘ | ✘ |

Dans le code aujourd'hui (20 octobre 2026) : `parties.create`, `parties.view_balance`, `cash.budget.read`, `transfers.create`, `transfers.credit`, `transfers.deliver` et `transfers.cancel` appliqués selon ce tableau ; les autres permissions des transferts, de la caisse et de l'export seront ajoutées avec leurs endpoints, et données aux rôles selon ce tableau.

## Authentification (validé, implémenté)

- Connexion par **email ou téléphone**, dans un seul champ. **Email obligatoire et unique** ; téléphone facultatif, unique s'il est renseigné, stocké en E.164 ; un numéro sans indicatif pays est refusé.
- Création de compte par le Superviseur avec **mot de passe temporaire**, changement obligatoire à la première connexion ; avant ce changement, le jeton n'autorise que le changement de mot de passe.
- Mot de passe : 8 caractères minimum dont au moins un chiffre. **Verrouillage après 5 échecs, pendant 15 minutes.** Même message d'erreur que le compte existe ou non ; le blocage n'est révélé qu'après le bon mot de passe.
- JWT de 15 minutes (identité seulement : `sub`, `sv` = SecurityVersion, `pwd_change`) + refresh token rotatif de 12 heures dans un cookie `HttpOnly`, `Secure`, `SameSite=Strict`, chemin `/api/v1/auth`, stocké haché (SHA-256). Rejouer un refresh token déjà utilisé révoque toute la session.
- **Option B** : permissions lues côté serveur, jamais dans le JWT. À chaque requête, la SecurityVersion du jeton est comparée à celle du compte (cache mémoire 30 s, vidé automatiquement à chaque modification du compte) : blocage, changement de rôle ou de mot de passe = effet immédiat.
- Changer son mot de passe ferme toutes les autres sessions. `GET /api/v1/auth/me` renvoie le compte connecté. 2FA TOTP du Superviseur : à faire.

## Caisse (validé)

- **Encaissement = + (l'argent entre), décaissement = − (l'argent sort).**
- Un hawala reçu du Sénégal est vu par l'utilisateur comme un encaissement même s'il est payé ici : en partie double, le compte tiers/Sénégal monte et la caisse physique baisse.
- Le type d'opération fixe le signe : l'agent ne choisit jamais + ou −.
- Virement interne entre comptes = deux lignes.

### Budget de la caisse (validé)

- Chaque agence a son budget, dans sa devise (Nouakchott en MRO, Dakar en FCFA) :
  `Budget caisse = espèces + banques + soldes débiteurs (tiers qui nous doivent) − soldes créditeurs (tiers à qui on doit) − transferts non livrés`.
- **L'agence enregistre son budget chaque jour** (clôture journalière) : photo datée du budget. Historique sur une période avec courbe.
- **Contrôle de cohérence** : analyse les opérations depuis le dernier budget enregistré et affiche des alertes à vérifier avant validation (ex. « taux de change inhabituel », voir « Taux et commission »).
- Ce budget par agence donne le résultat de chaque agence, sans règle de partage du bénéfice.
- **Seuls les tiers marqués « dans le bilan » comptent** dans les soldes débiteurs / créditeurs. Les transferts non livrés comptés sont ceux que **l'agence doit encore payer**, dans sa devise.
- Tous les chiffres sont lus dans le grand livre, rien n'est saisi à la main.
- **Corriger un solde** (Superviseur seulement, toujours tracé) : (1) modifier le **solde initial d'un tiers par agence, à tout moment**, comme dans l'ancienne app — passe par une écriture d'ouverture de la différence + audit avant / après ; (2) ajustement de solde avec motif obligatoire vers un compte « Écarts et ajustements » ; (3) contre-passation puis nouvelle saisie. Les budgets déjà enregistrés ne changent pas ; le contrôle de cohérence signale ces corrections.
- Permissions : `cash.budget.read`, `cash.budget.save`.

## Comptes de trésorerie (validé)

- Caisses espèces, banques, wallets (Bankily, Sedad, Wave, Orange Money…) d'une agence : les « méthodes de paiement ». Type : `Cash`, `Bank` ou `Wallet`. Devise = celle de l'agence, jamais saisie. Séparés des tiers (dans l'app actuelle, les banques étaient des tiers « considérés comme banque »).
- Chaque compte de trésorerie a son compte au grand livre, ouvert dans la même transaction.
- **Seul le Superviseur** crée, renomme, désactive ou réactive un compte de trésorerie (permission `treasury.manage`).
- **Un agent voit les comptes de son agence** (pour choisir la méthode de paiement) ; **leurs soldes seulement avec `cash.budget.read`**. Le Superviseur voit les deux agences.
- **Plusieurs caisses espèces possibles dans une même agence.** **Nom unique dans une agence.**
- **Désactiver exige un solde nul** ; un compte désactivé peut être **réactivé**.
- **Pas d'unité d'affichage par compte** : tous les comptes de Nouakchott s'affichent en MRO, ceux de Dakar en FCFA. Une saisie en MRU est seulement convertie en MRO (× 10).
- **Solde de départ** d'un compte de trésorerie à la mise en service : écriture d'ouverture contre le compte système « Soldes d'ouverture », comme le solde initial d'un tiers.

## Tiers (validé)

- Les tiers sont les personnes et comptes avec qui il y a du crédit : clients, fournisseurs, personnel, et aussi banques et wallets (Bankily, Sedad, banques…), qui forment la liste « Méthode de paiement ».
- **Rouge = il nous doit (on a de l'argent chez lui). Vert = on lui doit.** Total = rouges − verts.
- Champs : nom (max 30), téléphone, NNI, adresse, « dans le bilan », compte comptable (41 = clients), solde initial par agence (Nouakchott en MRO, Dakar en FCFA), note. (Le « genre » de l'app actuelle = banque / non banque : supprimé avec la séparation.) (« Considéré comme banque » : supprimé, voir plus bas.)
- Liste : filtres agence (verrouillé pour un agent), « dans le bilan / hors bilan / tous », solde, type de tiers ; totaux rouge et vert.
- Nouvelle app : séparer « Comptes de trésorerie » (caisses, banques, wallets) et « Tiers » ; plafond de crédit ; dédoublonnage par téléphone.

### Décisions du 19 octobre 2026 (validées)

- **Un tiers est commun aux deux agences** : une seule fiche, non rattachée à une agence. Il a **un solde par agence** (un compte au grand livre par agence : MRO à Nouakchott, FCFA à Dakar). Un agent voit la fiche, mais **seulement le solde de son agence**.
- **« Considéré comme banque » est supprimé** : les banques et wallets sont des comptes de trésorerie. Les anciens tiers « banque » deviendront des comptes de trésorerie à la migration (phase 8). **Séparation reconfirmée le 19 octobre 2026** (question « pourquoi pas des tiers comme dans l'app actuelle ? ») : un compte bancaire est notre argent, un tiers est quelqu'un d'autre ; le budget compte les banques à part ; les méthodes de paiement sont les comptes de trésorerie ; règles et droits différents. L'interface pourra montrer les deux dans des écrans proches.
- App actuelle, écran « Tiers » (« الأطراف ») : recherche (« كلمة البحث »), « 73 / 73 », total net en tête, puis sous la flèche : total vert, total rouge, agence, « Calcul général » (« حساب عام »), « Situation dans le bilan » (« الحالة في الميزانية » : Tous « الكل » / Dans le bilan « داخل الميزانية » / Hors bilan « خارج الميزانية »), « Solde » (« الرصيد » : Tous « الكل » / له « on lui doit » = vert / عليه « il nous doit » = rouge), « Genre du tiers » (« نوع الطرف »). Étiquettes : Client « عميل », Bilan « الميزانية ». Libellés à reprendre pour la traduction arabe.
- App actuelle : le filtre « Genre du tiers » de la liste = **Banque / Non banque / Tous** (la case « considéré comme banque »), pas Homme / Femme. Il disparaît avec la séparation.
- **Type de tiers = compte comptable** : Client (41), Fournisseur (40), Personnel (42), Autre.
- **Obligatoires : nom et type.** Facultatifs : téléphone (E.164, comme les comptes), NNI, adresse, note.
- **Pas de champ Homme / Femme** (supprimé le 19 octobre 2026) : le « genre » de l'app actuelle désignait banque / non banque, et les banques ne sont plus des tiers.
- **Téléphone unique** quand il est renseigné (doublon interdit). **NNI unique** quand il est renseigné, sans contrôle de format (les deux pays diffèrent).
- **Nom non unique** : deux tiers peuvent porter le même nom ; le téléphone les distingue.
- **Plafond de crédit par tiers et par agence**, dans la devise de l'agence, fixé par le Superviseur ; dépassement = **blocage** du crédit (appliqué en phase 3). Sans plafond fixé : pas de limite. **Le plafond est positif ou nul : 0 = aucun crédit autorisé ; vide = pas de plafond** ; un plafond négatif est refusé (validé le 19 octobre 2026).
- **Un agent peut créer un tiers** (permission `parties.create`, donnée au rôle Agent ; correction du 19 octobre 2026). **Seul le Superviseur le modifie** : fiche, solde initial, plafond, « dans le bilan », archivage (permission `parties.manage`).
- **Solde initial : jamais à la création**, toujours par l'action « solde initial par agence » (écriture d'ouverture de la différence), pour un seul chemin tracé. **Date d'opération choisie par le Superviseur** (date du jour par défaut), pour dater les soldes de départ au jour de mise en service lors de la migration (validé le 19 octobre 2026).
- Téléphone et NNI stockés en clair pour l'instant ; chiffrement au repos + empreinte pour l'unicité et la recherche en phase 6.
- **Pas de suppression : archivage**, seulement à solde nul dans les deux agences. Archiver ferme aussi les comptes du tiers au grand livre (plus aucune opération possible), désarchiver les rouvre : choix de cohérence avec la désactivation d'un compte de trésorerie (19 octobre 2026).
- **Soldes des tiers visibles avec la permission `parties.view_balance`** (comme les soldes de caisse avec `cash.budget.read`).
- **« Dans le bilan » vrai par défaut**, modifiable par le Superviseur seulement.
- **Relevé d'un tiers** (par agence) : solde au début de la période, chaque écriture avec le solde après la ligne, solde à la fin. **Sans dates : le mois en cours** (du 1er à aujourd'hui) ; `from` / `to` modifiables. Visible avec `parties.view_balance`, dans le périmètre de l'agence (validé le 19 octobre 2026).
- **Un solde à zéro n'est ni rouge ni vert** (validé le 19 octobre 2026) : il n'apparaît ni avec « عليه » ni avec « له », seulement avec « Tous » (l'app actuelle l'affichait en rouge).

## Transferts (validé)

- Montant de référence en FCFA (entier). **Montant MRO = FCFA × prix de vente / 5000**, arrondi selon « Arrondi ».
- Les taux de chaque transfert sont fixés par le Superviseur (voir « Taux et commission »).
- **C'est l'agence d'envoi qui crée le transfert** (validé le 20 octobre 2026) : l'agence source est celle de l'agent, verrouillée ; l'autre agence le voit arriver en « reçu », à livrer. Le Superviseur peut créer dans les deux sens. On choisit l'agence source et l'agence destination ; « envoyé » / « reçu » dépend de l'agence qui regarde.
- **Format du taux partout : « 5 000 FCFA = ? MRO »** (3600 = 5 000 FCFA valent 3 600 MRO).
- Sens : Envoyé / Reçu. Statuts : Non livré → Livré, ou Non livré → Annulé. Nouvelle app : « En attente de synchro » (hors ligne) et « Rejeté » (refus serveur).
- « Activer le crédit » : le client paie plus tard → dette sur le tiers (rouge).
- **Expéditeur et bénéficiaire : texte libre** (« معلومات المرسل » / « معلومات المتلقي »), tapé à chaque transfert, sans fiche (validé le 20 octobre 2026).
- **Crédit activé : la dette va sur un tiers choisi dans la liste des tiers** (pas forcément l'expéditeur) ; s'il n'existe pas, l'agent le crée comme nouveau tiers (validé le 20 octobre 2026). Le tiers passe en rouge dans l'agence d'envoi.
- « Répartition des paiements » : un paiement peut être réparti sur plusieurs comptes ; le total affecté doit égaler le montant, sauf si le crédit est activé.
- **Crédit partiel possible** : une partie payée (répartie sur des comptes de trésorerie), le reste à crédit ; part à crédit = montant − total payé (validé le 20 octobre 2026).
- **Annulation, application (21 octobre 2026)** : Superviseur seulement (`transfers.cancel`) ; non livré seulement (un transfert bloqué par les codes faux peut être annulé) ; motif obligatoire (500 caractères au plus), visible des deux agences ; date de l'annulation maintenant par défaut, jamais future ni avant le transfert ; contre-passation de l'écriture de création **à la date de l'annulation** (les budgets déjà enregistrés ne changent pas) : remboursement depuis les caisses de l'agence d'envoi, dette du crédit effacée, « Transferts à livrer » soldé. Annulé = verrouillé (plus de livraison) ; exclu des cartes.
- **Cartes de l'écran des transferts (lues sur l'app actuelle et validées le 21 octobre 2026)** : pour l'agence choisie A (l'autre : B), deux groupes de transferts, chacun dans les deux devises :
  « دخل · A » (entrées, devise de A) = payé à A pour les transferts envoyés par A ; « المبلغ المسلم · B » (livré, devise de B) = remis par B sur ces transferts ;
  « المبلغ المرسل · B » (envoyé, devise de B) = payé à B pour les transferts envoyés par B ; « صرف · A » (sorties, devise de A) = remis par A sur ces transferts ;
  « غير مسلمة » = ce que A doit encore payer ; « الربح » = somme des bénéfices de la période, dans les deux sens ; « 5,000 FCFA = ? » = taux du dernier jour de la période.
  Sorties et livré : transferts livrés seulement ; entrées et envoyé : tous les non annulés ; annulés exclus partout. Mêmes filtres que la liste (période sur la date d'opération, aujourd'hui par défaut ; recherche). Superviseur seulement (`profit.view`). Vérifié sur une capture : entrées NKC 371 868 637 MRO ≈ livré Dakar 514 058 901 FCFA × 3 616,98 / 5 000. La liste affiche aussi « 24 / 392 » (filtrés / tous).
- **Budget de la caisse, écart à trancher en phase 4** : l'app actuelle (« Bilan par caisse ») calcule espèces + banques + soldes débiteurs − soldes créditeurs, **sans** soustraire les transferts non livrés (capture du 21 octobre 2026 : 35 492 511 + 52 232 424 + 242 899 766 − 235 035 941 = 95 588 760 FCFA). La règle validée les soustrait : à confirmer avec Hamade avant la phase 4.
- **Livraison, règles d'application (validées le 20 octobre 2026)** :
  1. seule l'**agence de destination** livre ; le Superviseur peut livrer tout transfert ;
  2. un transfert **à crédit non remboursé se livre quand même** (la dette reste sur le tiers, dans l'agence d'envoi) ;
  3. le **code est saisi par tous, Superviseur compris**, pour une livraison normale (livrer sans code : action à part, pièce d'identité + motif) ;
  4. code faux **compté par transfert**, enregistré même si la livraison est refusée ; la réponse donne les essais restants ; au 5e, transfert bloqué (reste « non livré ») jusqu'au déblocage par le Superviseur ; les erreurs de saisie sont vérifiées avant le code et ne consomment pas d'essai ;
  5. date de livraison : maintenant par défaut, passée permise, jamais future ni avant le transfert ;
  6. caisse en négatif : livraison acceptée, la réponse liste les caisses passées en négatif avec leur nouveau solde ;
  7. livraison sur les **caisses actives de l'agence de destination** seulement ; livrer en créditant un tiers (dépôt du bénéficiaire) : pas pour l'instant (POINT OUVERT).
  Permission `transfers.deliver` (Superviseur, Agent, Caissier). Deux livraisons simultanées : une seule passe (verrou du transfert).
- **Ordre des tours de la phase 3 changé (20 octobre 2026)** : la livraison avant les cartes du Superviseur, pour que « sorties » et « montant livré » soient justes dès le départ.
- **Lecture des transferts (validée le 20 octobre 2026)** :
  1. l'agence de destination voit référence, statut, dates, expéditeur, bénéficiaire, note, les deux montants et le prix de vente ; **la répartition du paiement et le crédit (tiers, part à crédit) sont réservés à l'agence d'envoi et au Superviseur** ;
  2. liste sur la date d'opération, **aujourd'hui par défaut**, du plus récent au plus ancien, **50 par page** avec le nombre total ;
  3. filtres : statut, sens (envoyé / reçu vu depuis l'agence), agence (Superviseur), recherche texte (référence, expéditeur, bénéficiaire, note, sans tenir compte de la casse) ;
  4. avec `profit.view` : prix d'achat et **bénéfice de chaque transfert** en pleine précision (formules validées) ; sinon null ;
  5. le nom du tiers du crédit est affiché (agence d'envoi et Superviseur) ;
  le code de retrait n'est jamais renvoyé ; hors périmètre = 404.
- **Plafond de crédit, règles d'application (validées le 20 octobre 2026)** :
  1. contrôle sur le **solde du tiers après le transfert**, dans l'agence d'envoi : solde actuel + part à crédit ≤ plafond (un tiers en vert peut donc consommer ce qu'on lui doit, même avec un plafond de 0) ;
  2. **crédit total permis** : avec le crédit activé, la répartition peut être vide ;
  3. **crédit activé mais tout payé : refusé** (« rien à mettre à crédit ») ; payer plus que le montant reste refusé ;
  4. **le plafond bloque tout le monde, Superviseur compris** : pour aller au-delà, il relève d'abord le plafond.
  Permission `transfers.credit` (Superviseur, Agent). Tiers archivé refusé. Deux crédits simultanés sur un même tiers passent l'un après l'autre (verrou du compte du tiers dans l'agence).
- **Modifier un transfert non livré** : expéditeur, bénéficiaire et note par l'agence d'envoi ; montant, taux et répartition par le Superviseur seul, par contre-passation puis nouvelle écriture (validé le 20 octobre 2026).
- **Une seule livraison**, éventuellement répartie sur plusieurs méthodes ; pas de livraison en plusieurs fois. **Annuler un transfert livré : pas pour l'instant** (livré est verrouillé).
- **Une date d'opération dans le futur est refusée** (transferts et grand livre ; validé le 20 octobre 2026).
- **Livré = le bénéficiaire a reçu son argent.** Il peut être payé par n'importe quel moyen (espèces, Wave, Orange Money, Bankily, banque…) et la livraison peut être répartie ; le total livré doit égaler le montant.
- Livré et Annulé sont verrouillés ; une correction se fait par contre-passation ; l'annulation exige un motif ; chaque changement est historisé (qui, quand, avant / après).
- Référence lisible : **format de l'app actuelle** (ex. `TR-RS18-HG01-2026-TGNC-20261019-000043`), décision du 20 octobre 2026 ; sens des codes à préciser (voir Questions ouvertes).
- App actuelle : le bouton « + » noir en bas de l'écran de modification d'un transfert (تغيير الحوالة) ouvre simplement un **nouveau transfert vide** (écran إضافة الحوالة), comme « Ajouter » dans la liste. Il ne livre pas, n'annule pas et ne recopie rien : c'est un raccourci de création, sans effet sur le backend. Le garder ou non dans la nouvelle interface se décidera avec les maquettes.
- Écran des transferts : cartes du jour « entrées · Nouakchott », « sorties · Nouakchott » (MRO), « montant envoyé · Dakar », « montant livré · Dakar » (FCFA), taux du jour et bénéfice. **Ces cartes sont visibles par le Superviseur seulement.**

### Liste des transferts de l'app actuelle (capture du 20 octobre 2026)

- Onglets « الحوالة » (Transfert) et « الأرباح » (Bénéfices). Recherche, « 22 / 22 نتيجة », total des non livrés en tête (« غير مسلمة », en MRO), période « من / إلى » avec l'heure (par défaut aujourd'hui 00:00 → 23:59), bouton d'export, « + إضافة ».
- Filtres : « المرحلة » (étape = statut : non livré « غير مسلمة », livré, annulé « ملغى ») et « اتجاه الحوالة » (sens : envoyé, reçu « مستلم »).
- Panneau sous la flèche (capture du 20 octobre 2026) : sélecteur d'agence, puis les cartes « دخل · نواكشوط » (entrées, MRO), « صرف · نواكشوط » (sorties, MRO), « المبلغ المرسل · دكار » (envoyé, FCFA), « المبلغ المسلم · دكار » (livré, FCFA), « الربح » (bénéfice, MRO), « 5,000 FCFA = ? » (le taux, au format de toute l'app). Puis une section « الأرباح » (Bénéfices) « اليومية » (journalière) avec un champ « 5,000 FCFA = ? MRO » : dans l'app actuelle, le prix d'achat T est saisi là ; dans la nouvelle app, il vient des taux du jour fixés par le Superviseur.
- Carte : date et heure, statut, référence au format `#TR-RS18-HG01-2026-TGNC-20261019-000043` (différent du format validé `TR-NKC-20261019-000043` ; sens des codes RS18, HG01, TGNC inconnu), « المبلغ المراد تسليمه » (montant à livrer, ex. 10 000 000 MRO pour un transfert Dakar → Nouakchott), taux « 5 000 FCFA = ? » (ex. 3 590,00), expéditeur → bénéficiaire, sens « دكار → نواكشوط ».

### Création d'un transfert dans l'app actuelle (capture du 20 octobre 2026)

- Un seul écran : statut de départ « غير مسلمة » (non livré) ; **« المبلغ (FCFA) » obligatoire : on saisit le FCFA** ; taux « 5,000 FCFA = ? MRO » obligatoire (saisi par l'agent) ; « المبلغ (MRO) » calculé (« MRO » à côté n'est qu'une étiquette, pas un bouton) ; « تفعيل الدين » (activer le crédit) ; « توزيع طرق الدفع » (répartition des paiements, +), ligne « Espèce نقدا », « المجموع الموزع » (total réparti) ; « معلومات المرسل » et « معلومات المتلقي » (expéditeur, bénéficiaire, obligatoires) ; « ملاحظة » (note) ; « حفظ » (enregistrer).
- Bas de l'écran : « تاريخ العملية » (date de l'opération, **avec l'heure**, modifiable : ex. 20/10/2026 0:51) ; « المصدر » (source, obligatoire, ex. Nouakchott) et « الوجهة » (destination, obligatoire, ex. Dakar). Le sens n'est pas un choix envoyé / reçu : on choisit l'agence source et l'agence destination ; « envoyé » ou « reçu » dépend de l'agence qui regarde. Le transfert gardera date et heure ; le grand livre, la date d'opération.
- **Confirme la règle validée : le FCFA (entier) est le montant de référence, le MRO est calculé.** Le transfert affiché à 10 000 000 MRO au taux 3 590 correspond à 13 927 577 FCFA saisis (× 3590 / 5000 = 10 000 000,29, arrondi à 10 000 000).
- Différences voulues dans la nouvelle app : le taux n'est pas saisi par l'agent (prix de vente du jour automatique, modifiable par le Superviseur seul) ; création en 3 étapes ; code de retrait (nouveau).

### Code de retrait (validé)

1. Généré à la création : 6 chiffres aléatoires, affiché une fois, imprimé sur le reçu de l'expéditeur.
2. **L'expéditeur le transmet lui-même au bénéficiaire.**
3. L'agent de destination saisit le code pour livrer ; il ne peut jamais le lire.
4. Stocké haché ; 5 essais faux → transfert bloqué, déblocage par le Superviseur.
5. Code perdu : livraison par le Superviseur avec pièce d'identité + motif obligatoire (audit), ou nouveau code demandé à l'agence d'envoi.

## Bénéfice (validé)

T = prix d'achat du jour (taux du marché, MRO pour 5 000 FCFA), fixé par le Superviseur ; M = montant MRO du transfert, calculé au prix de vente. Bénéfice (commission) en MRO :

- On reçoit des FCFA, on paie des MRO : `B = F × T / 5000 − M`
- On reçoit des MRO, on paie des FCFA : `B = M − F × T / 5000`

Le bénéfice du jour = somme des transferts du jour. Réservé au Superviseur. Valeur de référence vérifiée sur l'app actuelle : **536 893 MRO** (avec T = 3629,88).

## Bilan général (validé)

`Résultat net = Trésorerie de toutes les caisses + Résultat tiers − Transferts non livrés − Capital initial` (tout en MRO au taux du bilan).

Valeur de référence vérifiée : capital 165 500 000, résultat net **−261 005 MRO (−0,16 %)**. Les transferts non livrés sont soustraits car l'argent est en caisse mais dû aux bénéficiaires. Écran : taux saisi, « Actualiser », « Enregistrer le bilan », montants envoyés du jour par agence. Réservé au Superviseur.

## Tableau de bord (validé)

Nombre de transferts, livrés / non livrés, **taux de livraison par agence**, rapport caisse / capital / bénéfice.

App actuelle (capture du 20 octobre 2026), écran Superviseur qui réunit bilan et tableau de bord :
- En tête : « الميزانية العامة » (bilan général) 164 132 887 MRO ; « رأس المال الأولي » (capital initial) 165 500 000 MRO ; la formule affichée « خزينة جميع الصناديق + نتيجة الأطراف - الحوالات غير المسلمة » (trésorerie de toutes les caisses + résultat tiers − transferts non livrés) ; « النتيجة الصافية » (résultat net) −1 367 113 MRO, −0,83 %, en rouge. Vérifié : 164 132 887 − 165 500 000 = −1 367 113.
- Champ « 5 000 FCFA = ? MRO » (taux du bilan, ex. 3600) ; boutons « تحديث » (Actualiser) et « حفظ الميزانية » (Enregistrer le bilan).
- Quatre cartes, par agence et dans sa devise (Nouakchott MRO, Dakar FCFA), avec un filtre de période (« اليوم » = aujourd'hui) : « المبلغ المرسل » (montant envoyé, avec le nombre de transferts), « الحوالات غير المسلمة » (transferts non livrés), « المبلغ المسلم » (montant livré), « نسبة التسليم » (taux de livraison, %).
- En bas, le détail de la formule : « خزينة جميع الصناديق » (trésorerie de toutes les caisses), « الحوالات غير المسلمة », « نتيجة الأطراف » (résultat tiers).
- Les cartes dépendent des transferts (phase 3) ; le bilan et son enregistrement sont en phase 5.
- Suite de l'écran : « خزينة جميع الصناديق » 277 744 197 MRO = « خزينة النقد » (espèces) 49 730 527 + « خزينة البنوك » (banques) 228 013 670 ; « الحوالات غير المسلمة » 0 ; « نتيجة الأطراف » −113 611 309 MRO = « ديون الأطراف - مستحقات الأطراف » (ce que les tiers nous doivent − ce qu'on leur doit) ; « الميزانية نواكشوط » 48 497 821 MRO et « الميزانية دكار » 160 604 259 FCFA (avec un lien vers le détail) ; « تفاصيل المكونات » (détail des composants, dépliable).
- **Vérifié sur ces chiffres : bilan général = budget de Nouakchott + budget de Dakar converti au taux du bilan** : 48 497 821 + 160 604 259 × 3600 / 5000 = 164 132 887,48 → affiché 164 132 887. Par les composants arrondis un à un : 277 744 197 − 113 611 309 = 164 132 888 : l'écart d'une unité confirme la règle d'arrondi 4 (pleine précision, seul le total affiché est arrondi). En phase 5, le bilan réutilisera le budget de chaque agence (phase 4).
- Les banques y sont comptées dans la trésorerie, pas dans le résultat des tiers : l'app actuelle sépare déjà banques et tiers dans ses calculs.
- Fin du détail des composants : « ديون الأطراف » (ce que les tiers nous doivent) 418 613 857 MRO ; une ligne cachée, déduite « مستحقات الأطراف » (ce qu'on leur doit) 532 225 166 MRO (418 613 857 − 532 225 166 = −113 611 309) ; « الأرباح المحفوظة » (bénéfices enregistrés) 430 074 MRO.
- « الموازين المحفوظة » (bilans enregistrés, 5) : onglets « Graphique » / « Liste » ; courbe du bilan général et courbe du capital initial par date (24/09, 25/09, 28/09, 10/10, 13/10/2026). Le capital initial varie d'un bilan à l'autre (≈ 157 M fin septembre, ≈ 165,5 M mi-octobre). C'est le « BalanceSnapshot » prévu en phase 5.

## Moteur comptable

- Grand livre en partie double ; seul module qui écrit les montants. Chaque écriture est équilibrée **par devise**.
- **Sens des soldes : débit − crédit.** Caisse : positif = argent en caisse. Tiers : positif = rouge (il nous doit), négatif = vert (on lui doit).
- **Comptes système par agence** (donc par devise) : Position de change, Transferts à livrer, Apports du patron, Écarts et ajustements, **Soldes d'ouverture** (contrepartie du solde initial d'un tiers, rattachée au capital comme les Apports du patron : ni gain ni perte).
- **Une opération peut être saisie avec une date passée** (date d'opération distincte de l'instant d'enregistrement en UTC).
- **Contre-passation : motif toujours obligatoire** ; **une contre-passation ne peut pas être contre-passée** (pour rétablir, on ressaisit l'opération) ; une écriture n'est contre-passée qu'une fois.
- **Une commande = une transaction, même sur plusieurs modules** (option A validée) : une seule connexion PostgreSQL par requête, partagée par les DbContext des modules d'argent (Ledger, Treasury, Parties, Transfers, Cash), transaction ouverte par un décorateur commun.
- Passage MRO ↔ FCFA via un compte système « Position de change ».
- Écritures immuables : jamais de UPDATE/DELETE, uniquement des contre-passations.
- Une commande = une transaction PostgreSQL (écritures + entité + audit).
- `Idempotency-Key` sur chaque création ; `If-Match` (version) sur les modifications ; verrou sur la livraison.

## Stack et conventions (décidées)

- Backend : ASP.NET Core .NET 10 (Minimal APIs), monolithe modulaire + Clean Architecture (Domain / Application / Infrastructure / Contracts par module), handlers maison (pas MediatR), FluentValidation, EF Core 10 + Dapper pour les rapports, PostgreSQL 17+ (un schéma par module), Identity + JWT court + refresh token rotatif + TOTP, SignalR, Hangfire, QuestPDF, Serilog + OpenTelemetry.
- Solution : `Hawala.slnx`, `Directory.Build.props` (nullable, warnings = erreurs), versions NuGet centralisées dans `Directory.Packages.props`, `Result` / `Error` (codes stables `Module.Raison` traduits par le frontend), `IClock` injecté, UUID v7 (`IdGenerator`).
- Frontend : React 19 + TypeScript + Vite, Tailwind + shadcn/ui, TanStack Query + Router, React Hook Form + Zod, i18next (fr, ar en RTL), vite-plugin-pwa + Dexie (file d'attente hors ligne), Recharts. Client API généré depuis OpenAPI.
- Modules : Identité & accès, Organisation, Tiers, Trésorerie, Grand livre, Taux, Transferts, Caisse, Bénéfice, Bilan, Notifications (temps réel entre agences), Audit & conformité. Un module n'appelle que les `Contracts` d'un autre (test d'architecture).
- API REST `/api/v1`, une route par cas d'usage (`POST /transfers/{id}/deliver`), erreurs en Problem Details (RFC 9457) avec un champ `code`. Tout endpoint est protégé par défaut, sauf ceux marqués anonymes.
- IDs UUID v7 ; identifiants techniques jamais affichés à l'utilisateur.
- Tests : xUnit + Testcontainers (vrai PostgreSQL) ; tests de référence sur 536 893 et −261 005 dès que les données source sont disponibles ; Playwright pour l'E2E.
- Hors ligne : seules les créations (transfert, encaissement, décaissement) vont en file ; livrer, annuler, modifier un taux et clôturer exigent le réseau.

## Hébergement (décidé)

Serveur en Europe accepté. Départ : 1 VPS OVHcloud en France (type VPS-2, 4 vCore / 8 Go) avec Docker Compose, Caddy derrière Cloudflare, sauvegardes `pg_dump` + WAL vers un stockage objet, restauration testée chaque mois. Budget visé : environ 15 à 20 € par mois.

## Hors périmètre V1

- Notifications clients par WhatsApp / SMS (reportées). En V1 le reçu PDF s'imprime ou se télécharge.

## Plan de réalisation

1. Fondations (solution, CI, Docker, Identité, Organisation, squelette PWA bilingue) — faits : authentification complète, permissions côté serveur, module Organisation (agences), gestion des comptes par le Superviseur (création, liste, bloquer / débloquer, rôle, agence, réinitialisation, profil). couleur d'agence. Restent : CI, squelette PWA ; 2FA du Superviseur reportée (à discuter).
2. Grand livre + Trésorerie + Tiers — **fait** (19 octobre 2026).
3. Transferts
4. Caisse et clôture journalière (approvisionnement / retrait du patron, budget de la caisse, contrôle de cohérence)
5. Bénéfice + Bilan
6. Sécurité avancée
7. Hors ligne + reçu
8. Migration depuis l'ancienne app + mise en service : import des tiers et de leurs soldes à la date de démarrage, **sans les anciens transferts**
9. Conformité (AML, HarisAI) ; plus tard WhatsApp / SMS

Valider les maquettes des écrans de transfert (création en 3 étapes, livraison) avec les utilisateurs avant la phase 3.

## Questions ouvertes

- **Transferts (phase 3)** : format de référence de l'app actuelle retenu (`TR-RS18-HG01-2026-TGNC-20261019-000043`) : que signifient RS18, HG01, 2026 et TGNC, et comment se génèrent-ils ?

- Bilan (phase 5) : « الأرباح المحفوظة » (bénéfices enregistrés, 430 074 MRO sur la capture) apparaît parmi les composants du bilan mais pas dans sa formule : qu'est-ce exactement, et entre-t-il dans le calcul ?
- Bilan (phase 5) : le capital initial varie d'un bilan enregistré à l'autre : saisi à la main à chaque bilan, ou calculé (capital de départ + apports du patron − retraits du patron) ?

- Migration (phase 8) : dans l'app actuelle, les deux agences sont aussi des tiers (« فرع دكار », « فرع انواكشوط »), avec un solde (ex. −148 116 001 MRO). La nouvelle app n'a pas de compte de dette entre agences : ces tiers ne sont pas repris tels quels ; comment traiter leur solde à la bascule ?
- Liste des tiers : rôle du bouton « Calcul général » (« حساب عام »).


- 2FA TOTP du Superviseur : **reportée**, à discuter avec l'utilisateur avant tout code (que peut faire un Superviseur qui ne l'a pas encore activée ?).
- Audit de « qui a créé / modifié ce compte » : à brancher quand le module Audit existera.

- Autres alertes du contrôle de cohérence, en plus du « taux inhabituel » ?

Quand une question est tranchée, mettre à jour ce skill (et le document d'architecture).