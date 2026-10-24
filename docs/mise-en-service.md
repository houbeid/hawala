# Mise en service — procédure de la nuit de bascule

Rédigé le 24 octobre 2026 (phase 8, tour 5). Pour Hamade et le Superviseur qui feront la bascule de l'ancienne app vers ETS EL-VETH.

Le principe : l'ancienne app s'arrête le soir du jour **J**, après la fermeture des deux agences. Le Superviseur charge dans la nouvelle app ce qu'il faut pour continuer : soldes des comptes de trésorerie, tiers et leurs soldes, transferts non livrés. Le lendemain (**J+1**), les agents travaillent dans la nouvelle app seulement. L'ancienne app reste consultable, mais plus personne n'y saisit (pas de double saisie).

Ce qui n'est **pas** repris : l'historique de l'ancienne app (transferts livrés, mouvements de caisse, anciens bénéfices). Il reste consultable dans l'ancienne app.

## Ce qu'il faut savoir avant de commencer

- **La date de démarrage ne se fixe qu'une fois.** Elle ne peut pas être dans le futur, et elle est refusée s'il existe déjà une opération datée avant elle. Une fois fixée, toute opération datée avant est refusée partout (caisse, transfert, solde initial, ajustement). On ne peut pas la corriger aujourd'hui (POINT OUVERT).
- **On fixe donc la date de démarrage = J**, le soir même, *avant* les imports. Les soldes de départ et les reprises sont datés de J ; les agents commencent le lendemain.
- **La base de production doit être vierge d'opérations.** Toute répétition se fait sur une autre base (copie de test), jamais sur celle de production : un seul essai de transfert daté d'avant J bloquerait la date de démarrage.
- **Les imports sont « tout ou rien ».** « Vérifier » ne crée rien et donne un rapport ligne par ligne. « Importer » n'est possible qu'après une vérification sans problème du même fichier. Si une ligne échoue, rien n'est créé.
- **Le budget de la nouvelle app soustrait les transferts non livrés** (décision du 21 octobre 2026), contrairement à l'ancienne app. Pour comparer : *budget nouvelle app + total des non livrés = budget ancienne app*.
- Tout se fait depuis l'écran **Administration › Mise en service** (Superviseur). La liste de contrôle en bas de cet écran dit ce qui est fait et ce qui reste à faire.

## Avant J (préparation, la semaine précédente)

Prérequis techniques (à préparer avec le déploiement, pas encore écrits) :

1. Serveur installé, HTTPS en place, app accessible depuis les deux agences.
2. Secrets posés en variables d'environnement : `Jwt:SigningKey`, `Encryption:Key` et `SeedSupervisor:*`. **`Encryption:Key` est sauvegardée hors du serveur** : sans elle, les téléphones et NNI des tiers sont illisibles.
3. Sauvegarde nocturne de la base en place, et une restauration déjà essayée une fois.
4. Caddy sert `sw.js`, `registerSW.js` et `manifest.webmanifest` sans cache (sinon les mises à jour de l'app n'arrivent pas).

Préparation dans la nouvelle app (sans aucune opération d'argent) :

5. Se connecter avec le Superviseur créé au démarrage ; changer son mot de passe temporaire.
6. **Comptes** : créer les comptes des agents, des caissiers et des consultations de chaque agence. Transmettre les mots de passe temporaires (affichés une seule fois). Ajuster si besoin les permissions, les tiers autorisés et les plafonds.
7. **Comptes de trésorerie** : créer chaque caisse, banque et wallet de chaque agence, avec le même nom que dans l'ancienne app. Pas de solde pour l'instant.
8. **Couleurs des agences**, si on veut les changer.

Répétition (sur une base de test, pas la production) :

9. Télécharger les deux modèles Excel depuis « Mise en service » : tiers (`Modele-import-tiers.xlsx`) et transferts non livrés (`Modele-reprise-transferts.xlsx`). La feuille « Aide » de chaque modèle rappelle les règles.
10. Les remplir une première fois à partir de l'ancienne app, et faire « Vérifier » pour corriger les problèmes à l'avance : téléphones mal écrits, doublons de téléphone ou de NNI, types inconnus, montants trop précis.
11. Faire la bascule complète sur la base de test, comparer les chiffres (voir « Vérifications »), puis jeter cette base.

Annoncer aux agents la date de J+1 et la règle : dès J+1, plus rien dans l'ancienne app.

## Le soir de J

Une personne lit les chiffres de l'ancienne app, une autre saisit dans la nouvelle. Noter chaque chiffre sur une feuille de bascule (une par agence).

### 1. Arrêter l'ancienne app

- Les deux agences ont fermé ; plus aucune saisie dans l'ancienne app à partir de maintenant.
- Relever dans l'ancienne app, pour chaque agence :
  - le solde de chaque caisse, banque et wallet ;
  - la liste des tiers avec leur solde dans chaque agence, et les totaux « عليه » et « له » ;
  - la liste des transferts **non livrés** et leur total à livrer ;
  - le budget de la caisse du jour.
- Garder ces relevés (captures ou exports) avec la date et l'heure.

### 2. Sauvegarder la base de la nouvelle app

Faire une sauvegarde de la base de production *avant* tout import (elle contient les comptes et les comptes de trésorerie, rien d'autre). C'est le point de retour si la bascule doit être recommencée.

### 3. Fixer la date de démarrage

« Mise en service » › **Date de démarrage** : choisir le jour J, puis « Fixer la date de démarrage » et confirmer. La liste de contrôle passe « Date de démarrage fixée » à « Fait ».

### 4. Soldes de départ des comptes de trésorerie

Pour chaque compte : **Comptes de trésorerie** › le compte › « Solde initial ». Saisir le solde relevé, avec la date J. Un découvert se saisit en négatif.

- À Nouakchott, tout est en MRO ; un montant lu en MRU se saisit avec la bascule MRU (× 10 automatique).
- À Dakar, le FCFA est un nombre entier.

### 5. Importer les tiers

« Mise en service » › **Tiers** :

1. Choisir le fichier rempli et la date **J**.
2. « Vérifier » : lire le rapport (tiers lus, tiers prêts, totaux par agence, problèmes « Ligne 4 · Solde · Nouakchott »). Corriger le fichier et revérifier jusqu'à zéro problème.
3. Comparer les **totaux par agence** du rapport avec les totaux de l'ancienne app.
4. « Importer N tiers » et confirmer.

Rappels du modèle :
- Nom (30 caractères au plus) et Type obligatoires : Client, Fournisseur, Personnel, Autre.
- Téléphone au format international (+222…, +221…) et NNI facultatifs, mais uniques.
- Solde positif avec le sens « Il nous doit » / « On lui doit » (عليه / له acceptés).
- Plafond vide = pas de plafond, 0 = aucun crédit.
- Les banques et wallets ne sont pas des tiers.

### 6. Reprendre les transferts non livrés

« Mise en service » › **Transferts non livrés** : même déroulé (fichier, date J, « Vérifier », comparer le total à livrer par agence avec l'ancienne app, « Importer »).

- Une ligne par transfert non livré, tel qu'il est dans l'ancienne app : référence (unique), date d'origine (jj/mm/aaaa hh:mm), agence d'envoi (NKC ou DKR), montant FCFA, montant MRO, prix de vente (facultatif), expéditeur, bénéficiaire, note.
- Les caisses ne sont pas touchées : l'argent est déjà dans leur solde de départ.
- Ces transferts gardent leur date d'origine. Ils sont exclus du Bénéfice et du Tableau de bord (déjà comptés dans l'ancienne app), et leur montant ne se modifie pas.

### 7. Capital initial

**Bilan par caisse** › « Capital initial » pour chaque caisse, avec le chiffre donné par le patron, dans la devise de la caisse.

### 8. Vérifications

Voir la section suivante. Tant qu'un écart n'est pas expliqué, on n'ouvre pas l'app aux agents.

### 9. Photos de départ

Une fois tout vérifié :
- **Budget de la caisse** › « Enregistrer le budget du jour », pour chaque agence ;
- **Bilan par caisse** › « Enregistrer le bilan », pour chaque caisse.

Ce sont les points de départ des historiques. La liste de contrôle doit alors être entièrement à « Fait », sauf le taux du jour, qui se fixe le matin de J+1.

## Vérifications (avant d'ouvrir aux agents)

Pour chaque agence, dans sa devise :

| Chiffre | Où le lire dans la nouvelle app | Doit être égal à |
| --- | --- | --- |
| Solde de chaque caisse, banque, wallet | Comptes de trésorerie | Le solde relevé dans l'ancienne app |
| Total « il nous doit » (rouge) et « on lui doit » (vert) | Tiers, totaux en haut de la liste (filtrés sur l'agence) | Les totaux عليه / له de l'ancienne app |
| Total des non livrés | Budget de la caisse, ligne « Transferts non livrés » | Le total relevé dans l'ancienne app |
| Budget de la caisse | Budget de la caisse | Budget de l'ancienne app **moins** le total des non livrés |

Compléments :
- Le **Journal d'audit** montre chaque solde de départ saisi (trésorerie et tiers).
- L'**export Excel des tiers** permet de comparer ligne à ligne avec la liste de l'ancienne app.
- Les tiers hors bilan (« Dans le bilan » = Non) ne comptent pas dans le budget.

**Si un chiffre ne correspond pas :**
- **Solde d'un tiers ou d'un compte faux** : le corriger par « Solde initial » sur sa fiche, avec la date J. La différence est écrite au grand livre et tracée dans l'audit.
- **Transfert repris en trop ou faux** : l'annuler (Superviseur, motif obligatoire). Le montant d'un transfert repris ne se corrige pas aujourd'hui (POINT OUVERT).
- **Tout est à refaire** : restaurer la sauvegarde de l'étape 2 et recommencer à l'étape 3. C'est le seul moyen de défaire la date de démarrage.

## Le matin de J+1

1. Le Superviseur fixe le **Taux du jour** (prix d'achat et prix de vente) avant le premier transfert : sans taux du jour, aucun transfert ne peut être créé.
2. Les agents se connectent, changent leur mot de passe temporaire, puis installent l'app sur leur téléphone (« Ajouter à l'écran d'accueil »).
3. Premier test réel, en présence du Superviseur : un petit transfert de Nouakchott vers Dakar, vu à Dakar sans rafraîchir, puis livré.

### Livrer un transfert repris

L'ancien code de retrait n'est pas connu de la nouvelle app. Quand le bénéficiaire se présente, deux voies (décision du 24 octobre 2026) :

- **Nouveau code** (cas normal) : l'agence d'envoi ouvre le transfert › « Nouveau code », puis envoie le reçu avec le code à l'expéditeur, qui le transmet au bénéficiaire. L'agence de destination livre avec ce code, comme d'habitude.
- **Livrer sans code** (Superviseur) : pièce d'identité du bénéficiaire et motif obligatoires, tracé dans l'audit.

## Les jours suivants

- Chaque soir, chaque agence enregistre son budget de la caisse (avec le contrôle de cohérence), et le Superviseur le bénéfice du jour.
- Pendant la première semaine, comparer chaque soir le budget avec ce que les agents comptent réellement en caisse.
- L'ancienne app reste consultable pour retrouver un ancien transfert, mais on n'y saisit plus rien.

## Points encore ouverts

- Corriger une date de démarrage déjà fixée (aujourd'hui : seulement par restauration de la sauvegarde).
- Corriger le montant d'un transfert repris.
- Annulation d'un transfert repris : elle efface la dette « à livrer » ; si l'expéditeur est remboursé en espèces, le remboursement se saisit ensuite en décaissement (à valider).
- Procédure de déploiement (serveur, Caddy, sauvegardes) : à écrire avec `deploy/`.
