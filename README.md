# Quality Automation — Financements

**Cible actuelle : QA_FIN sur localhost\SQLEXPRESS**, conformément à la demande du 12 octobre 2026. Configuration et dix YAML alignés ; les neuf scénarios modificatifs restent indisponibles. Les validations sur FIN_ALTEAL ci-dessous décrivent les essais précédents.

Application locale React/TypeScript + ASP.NET Core + bibliothèque Windows FlaUI appelée directement par l'API. Le bouton de lancement appelle le véritable exécutable installé ; les résultats proviennent du pilote, sans simulation du legacy dans le navigateur.

**État actuel :** le tableau de bord, l'API, le catalogue YAML, le lancement/attachement, les contrôles de fenêtre/modale, les logs, rapports, captures, historique et arrêt sont implémentés. La connexion physique QA_FIN et l'activation de l'onglet « Liste complète » ont été validées sur l'application réelle. L'ouverture depuis le navigateur quand cet onglet est absent est implémentée mais reste à tester. Recherche/ouverture/confirmation de 2261 restent à implémenter. La révision de taux et les huit autres opérations sont « Non disponible » et refusées côté API.

La cible actuelle pour la navigation en lecture seule est **QA_FIN** sur `localhost\SQLEXPRESS`. Le catalogue SQL physique est vérifié à chaque exécution sur les sessions du processus Financements. Les anciens essais sur FIN_ALTEAL et les dialogues de compatibilité restent documentés dans `docs/VALIDATION.md`.

## Démarrer

Prérequis : Windows, session interactive ouverte, .NET SDK 10, Node.js compatible Vite 7 (poste testé : Node 24), accès initial à NuGet/npm, Financements installé. L'API doit démarrer dans la session Windows interactive, hors session 0.

Depuis ce dossier, double-cliquer `start.cmd`, ou depuis PowerShell :

```powershell
.\QualityAutomation\start.cmd
```

Le script compile si les sorties manquent, démarre l'API sur **http://127.0.0.1:5178**, puis ouvre le navigateur par défaut avec un jeton local dans le fragment de l'URL. Le frontend retire ce fragment et garde le jeton uniquement dans sessionStorage. Aucun jeton dans l'URL des requêtes HTTP, ni dans les logs de lancement. `start.cmd -Build` force la compilation ; `start.cmd -NoBrowser` vérifie le démarrage sans navigateur. L'ExecutionPolicy Bypass du lanceur concerne seulement ce processus PowerShell, sans changement de politique système.

Le programme n'a pas encore été vérifié visuellement par l'agent dans Chrome/Edge : aucun navigateur de contrôle disponible et helper Computer Use indisponible. La compilation TypeScript/Vite et le service HTTP de la page ont été vérifiés.

### Commandes manuelles depuis la racine du dépôt

```powershell
dotnet build QualityAutomation/QualityAutomation.sln
Push-Location QualityAutomation/frontend
npm.cmd ci --no-audit --no-fund
npm.cmd run build
Pop-Location
dotnet run --no-build --project QualityAutomation/backend/Financements.QualityApi
```

L'API sert `frontend/dist` sur la même origine ; aucun serveur Vite n'est nécessaire pour utiliser l'application. Après un rebuild frontend, recharger la page. Pour arrêter le backend démarré en console : Ctrl+C. Le script démarre l'API en arrière-plan ; son PID peut être retrouvé en recherchant le processus dotnet dont la ligne de commande contient Financements.QualityApi.dll. Arrêter uniquement ce PID, jamais tous les processus dotnet ou Financements.

## Configuration

`agent-settings.json`, modifiable sur disque par l'opérateur du poste :

- `executablePath` : chemin existant de **Financements.exe**, aucun argument arbitraire transmis par HTTP.
- `sqlServer` : `localhost\SQLEXPRESS`, information configurée, pas une preuve de connexion.
- `targetDatabase` : `QA_FIN` pour la lecture seule, conformément à la demande actuelle. `QA_LEGACY` reste également autorisée en lecture seule ; le YAML doit correspondre exactement à la cible configurée. Aucun scénario modificatif n'est exécutable.
- `financementsConnectionLabel` : entrée DB Manager à identifier ; n'est pas utilisée comme preuve SQL ni pour sélectionner automatiquement une base.
- `windowTimeoutSeconds` : 1 à 120 secondes ; `runTimeoutSeconds` : 5 à 600 secondes, délai global supervisé par l'API.
- `automationBackend` : UIA2 sur ce poste après un échec COM UIA3 et une lecture UIA2 réussie ; UIA3 reste sélectionnable.

La vérification utilise SQLCMD ODBC 170 installé dans Program Files, l'authentification Windows et une lecture de `sys.dm_exec_sessions` sur l'instance locale. Elle exige le PID réel de Financements, le nom du poste, une session `Application Financements` et des catalogues correspondant tous à la cible. Elle conserve `connection-evidence.json` ; une absence de droits, un délai ou une autre base bloque le scénario. Aucun commutateur ne permet de déclarer la connexion vérifiée. Le titre et une connexion SQL indépendante ne suffisent pas.

Le démarrage de Financements peut présenter la dernière connexion et déclencher ses traitements propres. Le robot ne sélectionne aucune base automatiquement, ne répond pas aux dialogues de migration/incompatibilité, ne restaure pas de base et n'écrit pas en SQL. Un lancement SUCCESS signifie seulement qu'une fenêtre principale utilisable a été détectée, jamais qu'une connexion ou une opération métier est réussie.

## Utiliser le tableau de bord

1. Détecter Financements pour rafraîchir la disponibilité de l'agent et les processus de la session.
2. Lancer Financements pour créer une instance, ou s'attacher à l'unique instance de cette installation. Une instance d'une autre installation ou plusieurs instances sont bloquées.
3. Connecter Financements à QA_FIN, puis sélectionner « Ouvrir un emprunt », saisir un identifiant positif, cliquer Lancer. L'agent vérifie la connexion SQL et active « Liste complète ». Il bloque encore à la recherche non implémentée ; les étapes suivantes restent NOT_EXECUTED.
4. Consulter la console, l'historique, le rapport JSON, les logs et la capture PNG. La progression utilise SSE avec authentification par en-tête.
5. Arrêter demande l'annulation du pilote entre deux appels UI. Financements reste ouvert. Un appel FlaUI bloqué ne peut pas être interrompu de force dans le processus API ; la requête d'annulation reste visible et la place d'exécution reste occupée jusqu'au retour de cet appel. Un redémarrage de l'API peut être nécessaire si l'appel ne revient jamais.

SUCCESS : vérification terminée ; FAILED : erreur technique/délai ; BLOCKED : précondition non satisfaite/arrêt demandé ; NOT_EXECUTED : étape dépendante non réalisée. La base inconnue reste null dans le rapport, distincte de requestedDatabase.

## API et sécurité

L'API écoute uniquement sur 127.0.0.1:5178. Toutes les routes `/api` exigent `Authorization: Bearer <jeton local>`. Jeton aléatoire 256 bits dans `.local/api-token.txt`, exclu de Git, accès fichier limité à l'utilisateur Windows qui démarre l'API. Les origines étrangères, hôtes inattendus et requêtes cross-site sont refusés. Pas de CORS permissif, pas de commande shell arbitraire, pas de chemin de fichier fourni par le navigateur. CSP same-origin et cache désactivé pour les rapports authentifiés.

| Route | Fonction |
| --- | --- |
| GET /api/scenarios | Catalogue réel des YAML |
| GET /api/agent/status | Session, disponibilité, instance, cible configurée, connexion UNKNOWN |
| POST /api/agent/launch | Commande réelle de lancement/attachement supervisée |
| POST /api/runs | `{ "scenarioId": "ouvrir-emprunt", "emprunt": 2261 }` |
| GET /api/runs | Historique persistant |
| GET /api/runs/{id} | Snapshot de progression |
| POST /api/runs/{id}/cancel | Demande d'arrêt |
| GET /api/runs/{id}/events | Flux SSE authentifié |
| GET /api/runs/{id}/logs | Événements structurés |
| GET /api/runs/{id}/result | Rapport JSON |
| GET /api/runs/{id}/screenshots | Noms des captures disponibles |
| GET /api/runs/{id}/screenshots/error.png | Capture authentifiée |

Une exécution active par API ; mutex Windows de bureau dans le pilote pour éviter deux exécutions concurrentes dans la session. `Financements.LegacyPilot` est une bibliothèque chargée par l'API ; les appels FlaUI tournent sur un **thread STA d'arrière-plan**, jamais sur un thread HTTP. L'annulation et le délai sont vérifiés entre les appels UI. Si un appel COM ne revient pas, l'API reste disponible mais refuse une nouvelle exécution tant que le thread occupe la place. Les exécutions interrompues par un redémarrage sont marquées FAILED à la récupération de l'historique.

## Scénarios et preuves

Dix fichiers dans `scenarios/`. La disponibilité est déterminée côté serveur, pas par un champ YAML manipulable. Le parcours de navigation doit conserver les six étapes prises en charge, faute de quoi le chargement est refusé. Les paramètres métier des scénarios préparés sont visibles mais aucun moteur de modification n'est branché.

Chaque exécution dans `results/<UUID>/` conserve `result.json`, `agent-result.json` lorsque produit, `agent.jsonl`, la configuration figée et `error.png` si capture possible. Le dossier results est exclu de Git. La capture contient le bureau réel du poste et doit rester locale sauf partage voulu par l'opérateur.

L'ancienne commande autonome `inspect` est remplacée par la méthode de bibliothèque `DesktopDiagnostics.Inspect`, à appeler depuis un thread STA. Elle n'est pas exposée comme route HTTP.

Tests HTTP non modificatifs, API démarrée :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File QualityAutomation/tests/test-api.ps1
# Ajouter -RunId <UUID terminé> pour tester rapport, logs, SSE et captures.
```

## Résultats et suite

Voir `docs/VALIDATION.md` pour les preuves et `docs/ANALYSE_FINANCEMENTS.md` pour les classes/méthodes/contrôles et raccourcis réels.

Le scénario `ouvrir-emprunt` recherche le numéro saisi par l'utilisateur dans **Liste complète**, exige une ligne unique dans la colonne **N° fiche**, l'ouvre par double-clic et confirme le numéro dans le titre et l'onglet sélectionné. La recherche native peut aussi renvoyer des contrats ou programmes contenant le même nombre : ces lignes ne sont pas ouvertes. Si les contrôles WebView ne sont pas accessibles ou sont ambigus, l'exécution se termine `BLOCKED`. Le parcours complet reste à vérifier sur une session Financements connectée à QA_FIN ; les scénarios modificatifs restent désactivés.
#   Q u a l i t y A u t o m a t i o n  
 #   Q u a l i t y A u t o m a t i o n  
 
