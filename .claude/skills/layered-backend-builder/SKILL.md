---
name: layered-backend-builder
description: Méthode de construction incrémentale d'un backend en couches (Clean Architecture / Domain-Application-Infrastructure-API) fichier par fichier, avec tests systématiques et validation avant toute modification de code déjà testé. À utiliser chaque fois que l'utilisateur démarre ou poursuit un projet backend structuré en couches (C#/.NET, mais principes portables), demande explicitement de construire "comme on l'a fait avant", ou que la tâche implique plusieurs fichiers interdépendants nécessitant un ordre de dépendance strict (Domain avant Application avant Infrastructure avant API/présentation). Déclenche aussi sur "on continue le backend", "prochain fichier", "on passe à [couche suivante]", ou toute demande de construire un service testé de façon incrémentale plutôt qu'en un seul gros jet.
---

# Layered Backend Builder

Méthode éprouvée sur le projet HarisAI fraud-backend (.NET 8, Clean Architecture, 175 tests) — construction d'un backend couche par couche, fichier par fichier, avec un principe directeur : ne jamais accumuler de code non testé, ne jamais modifier silencieusement du code déjà validé.

## 1. Ordre de construction — respecter la direction des dépendances

Construire strictement de l'intérieur vers l'extérieur. Chaque couche ne dépend que de celles qui la précèdent, jamais l'inverse.

```
Domain          zéro dépendance externe — entités, value objects, règles métier pures
     |
Application     ports (interfaces) + cas d'usage (Commands/Queries) — dépend de Domain uniquement
     |
Infrastructure   implémentations concrètes des ports — dépend de Domain + Application
     |
API / Worker      assemblage final (DI, Controllers, BackgroundService) — dépend de tout
```

À l'intérieur de chaque couche, respecter aussi l'ordre de dépendance interne : dans Domain, Enums avant ValueObjects avant Entities avant Events (les Entities utilisent les ValueObjects). Dans Application, les Interfaces avant les Commands/Queries qui les consomment.

**Pourquoi cet ordre et pas un autre** : chaque fichier écrit doit pouvoir compiler et être testé immédiatement avec ce qui existe déjà. Écrire Infrastructure avant Application produirait du code qui implémente des interfaces qui n'existent pas encore.

## 2. Cadence fichier par fichier

- Un fichier (ou un petit groupe de fichiers fortement couplés qui ne compileraient pas séparément) par tour de conversation.
- Après chaque fichier : présenter le fichier, expliquer 2-4 décisions de conception concrètes prises dedans (pas une paraphrase du code — le pourquoi, pas le quoi), puis attendre confirmation avant de continuer.
- Ne jamais anticiper plusieurs fichiers à l'avance sans validation intermédiaire, même si la suite semble évidente.
- Si un fichier révèle qu'un autre fichier (pas encore écrit) est nécessaire pour compiler, le signaler explicitement et proposer de l'écrire en premier plutôt que de laisser une référence cassée.

## 3. Stratégie de test — adapter l'outil à ce qu'on prouve réellement

Ne jamais tester par habitude — choisir l'outil qui prouve la garantie réellement recherchée.

| Ce qu'on doit prouver | Outil | Ne jamais faire |
|---|---|---|
| Une règle métier pure (Domain, cas d'usage Application) | xUnit + mocks (Moq) | Mocker quelque chose qui n'a pas de vraie dépendance externe |
| Un appel HTTP sortant vers un service tiers | Serveur HTTP simulé (WireMock.NET) | Supposer le format de réponse sans le vérifier via une vraie requête capturée |
| Une requête SQL dont la correction dépend du moteur réel (concurrence, verrous, syntaxe spécifique) | Vraie base via conteneur jetable (Testcontainers) | Un fournisseur de test en mémoire qui ne supporte pas la syntaxe réelle utilisée |
| Une validation cryptographique / un pipeline d'authentification | Vrai pipeline HTTP (TestServer / WebApplicationFactory) | Appeler une méthode protégée par réflexion pour contourner le pipeline |
| Le cycle de vie d'un service hébergé (BackgroundService, Worker) | Démarrage/arrêt réel du service (StartAsync/StopAsync) avec un vrai conteneur DI | Tester uniquement une méthode privée extraite artificiellement |

**Principe résumé** : si le comportement testé constitue lui-même la garantie technique recherchée (concurrence réelle, sécurité cryptographique réelle, vrai cycle de vie), un double fictif est insuffisant — utiliser le vrai composant, isolé mais réel.

**Optimisation** : si plusieurs classes de test consomment la même ressource lourde (conteneur Docker), la partager via une fixture de collection plutôt qu'une instance par classe — un gain de temps d'exécution significatif sans perte d'isolation, à condition de nettoyer les données en début de chaque test.

## 4. Le garde-fou le plus important : signaler avant de toucher du code déjà testé

Dès qu'une nouvelle fonctionnalité nécessite de modifier un fichier qui a déjà des tests passants :

1. Le dire explicitement avant de coder — quel fichier, pourquoi, quel est le risque de régression.
2. Si le changement est mineur et évident (ajouter un paramètre à une méthode déjà comprise), proposer de procéder directement.
3. Si le changement révèle une ambiguïté de conception ou touche plusieurs fichiers déjà testés en cascade, poser une question de clarification avant d'agir plutôt que de deviner.
4. Après modification, mettre à jour systématiquement les tests concernés dans le même tour — ne jamais laisser une suite de tests rouge d'un tour à l'autre.

## 5. Ports avant implémentations — l'inversion de dépendance appliquée concrètement

Dans la couche Application, définir une interface (port) uniquement quand un cas d'usage réel en a besoin — jamais par anticipation d'un besoin hypothétique. Chaque méthode d'un port doit avoir un consommateur identifié dans le code déjà écrit ou sur le point de l'être.

Symétriquement, ne pas construire d'abstraction générique (ex: un cache clé-valeur générique) sans un vrai cas d'usage confirmé — préférer un scope minimal, documenté comme volontairement limité, extensible plus tard avec un vrai besoin plutôt qu'en préparation d'un besoin supposé.

## 6. Quand un vrai problème de conception apparaît en cours de route

Il arrive qu'écrire un fichier révèle un problème que la conception initiale n'avait pas anticipé (exemple vécu : une même exception générique levée dans deux contextes sémantiquement différents, empêchant un middleware générique de choisir le bon code HTTP sans inspecter le message).

Dans ce cas :
1. Arrêter, expliquer le problème concrètement avec un exemple.
2. Présenter la solution correcte et son coût réel (nombre de fichiers déjà testés à modifier).
3. Proposer une alternative plus rapide mais moins propre si elle existe, avec son compromis assumé.
4. Laisser l'utilisateur choisir plutôt que d'imposer silencieusement la solution la plus rigoureuse.

## 7. Honnêteté documentée — les limites vivent dans le code, pas seulement dans la conversation

Chaque décision de report (fonctionnalité non implémentée faute d'information, comportement dégradé accepté comme compromis) doit être documentée directement dans un commentaire du fichier concerné, pas seulement mentionnée oralement. Un futur lecteur du code doit comprendre le compromis sans avoir accès à l'historique de la conversation.

Formulation type à réutiliser :
```
// POINT OUVERT : [ce qui manque]. [Pourquoi ce n'est pas fait maintenant].
// [Ce qu'il faudra faire quand l'information sera disponible].
```

## 8. Validation finale — sortir du mock pour confirmer le contrat réel

Une fois les couches construites et testées unitairement, chercher activement à valider contre le vrai système externe si le projet en dépend (API tierce, service partenaire). Les tests avec doublures ne peuvent jamais détecter une divergence de contrat réelle (ex: un préfixe de route manquant, une clé d'authentification par défaut différente de celle supposée) — seul un test contre le vrai système la révèle.

Si le vrai système est disponible dans l'environnement de travail, l'installer et le lancer réellement plutôt que de continuer à supposer son comportement. Documenter chaque divergence découverte et corriger immédiatement le code concerné plus les tests qui la simulaient à tort.

## 9. Anti-patterns à éviter systématiquement

- Écrire un fichier de test qui suppose le comportement d'un composant sans avoir vérifié ce comportement dans le vrai code (ex: tester qu'une classe rejette un format sans vérifier où cette validation vit réellement).
- Dupliquer une même logique de traduction d'erreur dans plusieurs Controllers plutôt que de la centraliser dès que le deuxième cas apparaît.
- Ajouter une dépendance croisée entre deux composants destinés à des déploiements séparés (ex: un Worker qui dépendrait de types de la couche présentation d'une API) — préférer une implémentation neutre documentée plutôt qu'un couplage architectural incorrect.
- Continuer à construire de nouvelles couches sans revenir combler une lacune méthodologique identifiée (ex: un pipeline de validation défini mais jamais réellement branché) — la combler dès qu'elle est détectée, avant de continuer.

## 10. Fin de session — capturer l'état pour la reprise

À la fin d'une session de travail longue, produire ou mettre à jour un document d'architecture qui reflète fidèlement l'état réel du code (pas l'état prévu initialement) : structure des couches, décisions clés avec leur raisonnement, et une liste honnête des points ouverts. Ce document sert de point de reprise pour la session suivante — il doit permettre à quelqu'un sans le contexte de la conversation de comprendre où en est le projet.
