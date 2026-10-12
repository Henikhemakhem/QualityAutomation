# Validation — 9 octobre 2026

Les essais ci-dessous distinguent compilation, contrôles HTTP et interactions avec le véritable processus Windows. Aucun scénario métier modificatif, aucune écriture SQL et aucune modification C++/MFC.

## Phase 1 — Analyse

Fichiers : `docs/ANALYSE_FINANCEMENTS.md`, basé sur `../../ANALYSE_LEGACY.md` et les sources revérifiées. Classes : CDTEprtView, CLEmpruntUI, CEpOpRevTauxDlg, CEpOpMouvDlg, CGestBigListeEmpt, CGestAppliContext. Méthodes et contrôles documentés, Ctrl+1/Ctrl+Tab et recherche par Entrée relevés dans les ressources/gestionnaires. Aucun test métier déduit d'une lecture source.

## Phases 2 et 4 — API et agent réel

Fichiers : `backend/Financements.QualityApi/{Program.cs,RunCoordinator.cs,appsettings.json,Financements.QualityApi.csproj}`, `Financements.LegacyPilot/DesktopRunner.cs`, ajout de la commande execute dans Program.cs, `agent-settings.json`, `start.ps1`, `start.cmd`, `QualityAutomation.sln`.

Commande : `dotnet build QualityAutomation/QualityAutomation.sln`, puis `QualityAutomation/start.cmd`. Les deux projets compilent sans avertissement ni erreur. Le script start.ps1 -NoBrowser a été exécuté avec succès. L'API répond sur 127.0.0.1:5178 et l'historique est récupéré après redémarrage.

Essais réels :

- Détection du processus de la session ; lancement du chemin d'installation réel ; attachement à une instance existante.
- Un premier échec de lecture MainModule a conduit à l'utilisation de QueryFullProcessImageName avec PROCESS_QUERY_LIMITED_INFORMATION.
- Une instance devenue indisponible a produit un délai et un rapport FAILED. Un essai UIA3 a rencontré l'erreur COM 0x80040201 ; la lecture UIA2 a ensuite abouti. La configuration actuelle sélectionne UIA2, UIA3 restant disponible.
- L'inventaire UIA2 a réellement détecté `Financements 25.1` / `Loan2000Class`, désactivée par la modale `Base incompatible`. La modale et son message sont lus et bloquent le lancement utilisable, sans clic automatique.
- Des résultats de lancement ont été persistés, dont SUCCESS lorsque la fenêtre principale était utilisable, et BLOCKED/FAILED lors des erreurs. Un SUCCESS de lancement ne signifie pas connexion réussie ni fiche ouverte.
- Correction testée : la fin de l'agent était retardée par des handles de flux hérités par le processus GUI. L'API s'arrête désormais sur son événement final, vérifie code retour et rapport puis libère l'exécution ; elle ne dépend pas de la fermeture de Financements.

Limite actuelle : installation/version de base incompatible et connexion physique non confirmée. Aucun dialogue d'identification ou de changement de base traité automatiquement.

## Phase 3 — Frontend

Fichiers : frontend/package.json et package-lock.json, index.html, tsconfig.json, vite.config.ts, src/main.tsx, src/style.css. Interface française : tableau de bord local, état agent/application, dix scénarios, sélection, paramètres, étapes, logs, statuts, historique et téléchargements authentifiés.

Commandes : npm.cmd ci et npm.cmd run build depuis frontend. Vérification TypeScript et compilation Vite réussies ; GET de la page servie par l'API : HTTP 200.

**Vérification visuelle non réalisée par l'agent** : le navigateur de contrôle ne propose aucun navigateur connecté ; Computer Use échoue « native pipe is unavailable », y compris après retry/reset. Aucune validation visuelle ni clic dans Chrome/Edge n'est revendiqué.

## Phase 5 — Scénario ouvrir-emprunt

Fichiers : `scenarios/ouvrir-emprunt.yaml`, DesktopRunner et RunCoordinator. Six étapes prévues et résultats individuels. Seuls lancement/attachement, vérification de fenêtre/modale et garde-fou de connexion sont implémentés actuellement. L'étape de connexion refuse toujours tant qu'une preuve fiable du catalogue actif n'est pas disponible.

Exécution API réelle `0ddc3aeb-3a53-4101-89f4-2b27bc7bf78b` :

| Étape | Résultat |
| --- | --- |
| lancer_financements | BLOCKED : Base incompatible, QA_FIN affichée en 26.10.0, version attendue 25.13 |
| verifier_connexion | NOT_EXECUTED |
| ouvrir_gestion_emprunts | NOT_EXECUTED |
| rechercher_emprunt | NOT_EXECUTED |
| ouvrir_fiche | NOT_EXECUTED |
| verifier_fiche_ouverte | NOT_EXECUTED |

Rapport global BLOCKED, database = null, emprunt = 2261, erreur explicite et error.png conservés. Aucune fiche ouverte par le robot. L'absence de sélection SQL autorisée n'est pas transformée en succès.

Prochaine étape : fournir un exécutable/base compatibles et l'entrée DB Manager physiquement QA_LEGACY, puis mesure UI et implémentation de la navigation exacte. FIN_ALTEAL et QA_FIN ne sont pas assimilées à QA_LEGACY.

## Phase 6 — Rapports, flux et protections

Fichiers : results/<UUID>/, tests/test-api.ps1. Résultats et captures locaux exclus de Git.

Tests HTTP réalisés : 401 sans jeton ; 403 pour Origin étrangère ; 409 pour demande concurrente **en lecture seule** ; GET scénario/historique/rapport/logs ; flux SSE avec content-type text/event-stream et trame data ; téléchargement authentifié de la capture (200) ; UUID inexistant (404). Le script test-api.ps1 -RunId 0ddc3aeb-3a53-4101-89f4-2b27bc7bf78b réussit. Demande d'arrêt d'une exécution active enregistrée BLOCKED ; aucune commande de fermeture/sauvegarde injectée dans Financements.

Token local protégé par ACL utilisateur ; loopback et origine exacte ; fichiers accessibles seulement par UUID et nom de capture autorisé ; aucune entrée HTTP d'exécutable, commande, connexion SQL ou chemin arbitraire. Mutex de bureau, exclusion API et deadline globale.

## Ouverture par numéro de fiche — 12 octobre 2026

`EmpruntPage.SearchByNumber` saisit le numéro demandé dans le champ de recherche visible de Liste complète et n'accepte qu'une ligne unique sous l'en-tête `N° fiche`. `OpenEmprunt` double-clique cette ligne ; `VerifyOpened` attend le même numéro dans le titre de la fenêtre et dans l'onglet sélectionné. Les trois étapes remontent leurs statuts dans le rapport et l'historique API. Une ligne ou un contrôle absent ou ambigu produit `BLOCKED` sans double-clic sur une autre fiche.

Compilation `Financements.QualityApi.csproj` avec sa référence directe à `Financements.LegacyPilot` : succès, 0 avertissement, 0 erreur. Le test HTTP `test-api.ps1` n'a pas pu lire `.local/api-token.txt` depuis cette session (ACL Windows) ; aucun résultat HTTP nouveau n'est revendiqué. L'application Financements n'était plus ouverte lors de cette validation : le double-clic sur une fiche QA_FIN reste à vérifier en session connectée.

## Phase 7 — Opérations préparées

Neuf YAML supplémentaires et détails affichés : taux, RA partiel/total, diminution, lissage, report, Livret A, création, modification. Non disponibles dans le frontend et refusés par le catalogue serveur. Aucun handler modificatif branché ni opération simulée.

Le contrôle automatique d'approbation a refusé un test qui aurait envoyé revision-taux pour vérifier le rejet concurrent : risque de mutation sur une connexion QA non vérifiée en cas de défaut du garde-fou. La commande refusée n'a pas été exécutée ; la concurrence a été testée avec ouvrir-emprunt uniquement.

## Mise à jour — cible FIN_ALTEAL, 9 octobre 2026

La demande actuelle remplace QA_LEGACY par **FIN_ALTEAL** pour la lecture seule. `agent-settings.json` et `scenarios/ouvrir-emprunt.yaml` correspondent à cette cible ; l'API vérifie cette correspondance. Les neuf scénarios modificatifs restent indisponibles et leurs anciennes cibles QA ne sont pas transformées en autorisation de mutation.

Après correction de version, une fenêtre utilisable `Financements 25.1 - FIN_ALTEAL - [ Liste complète]` a été observée pour le PID 14656. Une lecture SQL de `sys.dm_exec_sessions` sur `localhost\SQLEXPRESS` a confirmé les sessions 52, 75, 78, 81 et 83 de ce PID, toutes sur FIN_ALTEAL ; la session 75 portait `Application Financements`. Le nouveau vérificateur lit ces métadonnées avec SQLCMD, exige aussi le nom du poste et conserve une preuve par exécution. Cette observation préalable confirme la connexion à cet instant ; elle ne constitue pas une validation du nouveau vérificateur dans un scénario complet.

L'application a ensuite été fermée. L'essai API `7d9f1eb8-d088-4baf-8b60-9951b93d3636` a lancé Financements sur `Login` et terminé FAILED après attente de la fenêtre principale. Le pilote détecte désormais cette fenêtre observée et signale un blocage explicite demandant la connexion manuelle à FIN_ALTEAL. Aucun identifiant n'a été renseigné par le robot. La vérification intégrée du catalogue et la navigation complète restent à tester après connexion ; la navigation liste/fiche reste non implémentée.

Le nouvel essai `a7501cee-8e15-4dff-a968-8123abb1ba61` confirme le résultat BLOCKED sur Login en 90 ms, avec FIN_ALTEAL comme cible demandée, database = null et les cinq étapes suivantes NOT_EXECUTED.

Compilation solution .NET et frontend TypeScript/Vite réussie sans erreur. API redémarrée avec le nouveau code ; cible FIN_ALTEAL chargée. `tests/test-api.ps1 -RunId 7d9f1eb8-d088-4baf-8b60-9951b93d3636` réussi : authentification, origine, catalogue, rapport, logs, SSE et capture. Aucune opération modificative effectuée.

Référence : [Microsoft — métadonnées des sessions SQL](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-objects/sys-dm-exec-sessions-transact-sql?view=sql-server-ver17).

## Navigation — 12 octobre 2026

Le blocage constant de `ouvrir_gestion_emprunts` est remplacé par `EmpruntPage.OpenList`. Sélecteurs observés avec UIA2 : barre d'onglets enfant directe, AutomationId `1000`, onglet `Liste complète`, pattern SelectionItem. Le succès exige l'onglet sélectionné et le titre `[ Liste complète]`. Aucun parcours global SUCCESS n'est déclaré tant que la fiche exacte n'est pas confirmée.

Essais directs `results/navigation-liste-ciblee` (PID 37796) et `results/navigation-liste-depuis-accueil` (PID 4440) : lancement/attachement, vérification physique FIN_ALTEAL et activation de l'onglet SUCCESS. La recherche reste BLOCKED, ouverture/vérification de fiche NOT_EXECUTED. Preuves de connexion et rapports conservés. Le second essai avait déjà un onglet ouvert : son nom de dossier ne prouve pas la création d'une liste depuis l'accueil.

Le chemin de repli quand l'onglet manque parcourt les contrôles natifs observés : Navigateur → Financements → Dette → Emprunts. Il développe l'arbre, exige une entrée unique Liste complète visible/activable, puis utilise le double-clic d'ouverture prévu par MainTree.OnDblclk. Ce repli reste à valider sur le poste. Les recherches UIA globales ont été remplacées par des recherches sur les enfants directs pour éviter une traversal WebView lente.

L'inventaire profond séparé a rencontré des éléments UIA périmés ; il a été retiré de l'exécution normale. La sonde conserve désormais les propriétés du parent avant de parcourir ses enfants, accepte une profondeur optionnelle et fournit `inspect-list` pour cibler la liste. Les instances Financements ont changé pendant les essais puis ont été absentes : impossible de valider la recherche et la fiche pendant cette session. Une nouvelle instance a ensuite affiché QA_FIN ; la cible autorisée demeure FIN_ALTEAL. Aucun scénario modificatif exécuté.

Le test API `1823605b-5200-4595-b444-e8b86f448adf` sur cette instance confirme le refus à `verifier_connexion` : catalogue actif différent de FIN_ALTEAL, aucune navigation exécutée. Les tests HTTP de rapport/logs/SSE/capture et protections passent sur ce rapport BLOCKED. Build agent réussi sans avertissement ni erreur ; l'API utilise ce binaire pour les prochaines exécutions.

## Documentation technique consultée

## Pilote intégré à l'API — 12 octobre 2026

`Financements.LegacyPilot` est désormais une bibliothèque référencée par `Financements.QualityApi`. L'API lance chaque exécution sur un thread STA d'arrière-plan, reçoit les événements par callback et conserve `result.json`, `agent-result.json`, `agent.jsonl`, la configuration figée et les captures. La sonde reste accessible comme méthode `DesktopDiagnostics.Inspect`, sans exécutable autonome.

Le premier essai intégré `117ca1f9-5492-495f-a0f3-8adb55ef586b` a produit les événements, le rapport et la capture attendus ; Financements était sur Login, donc résultat BLOCKED et étapes SQL/navigation NOT_EXECUTED. Le test `tests/test-inprocess.ps1` a obtenu HTTP 409 pour une seconde exécution active, puis BLOCKED après annulation et aucune étape dépendante exécutée. Le rapport `c8eb3b0a-06cb-4600-8dbe-7063a3499ef5`, ses logs, son flux SSE et sa capture passent `tests/test-api.ps1 -RunId` ; l'événement d'annulation figure dans `agent.jsonl`. Builds .NET et frontend réussis sans avertissement ni erreur.

L'annulation est coopérative : si un appel FlaUI/COM reste bloqué, l'API demeure dans son processus et réserve la place d'exécution jusqu'au retour de l'appel. Le redémarrage de l'API peut alors être nécessaire. Aucun scénario modificatif exécuté.

## Changement de cible — QA_FIN, 12 octobre 2026

La demande « modifier base à QA_FIN » remplace FIN_ALTEAL comme cible actuelle. Configuration agent, libellé de connexion, dix YAML (y compris revision-taux), garde-fous API/agent et affichage par défaut alignés sur QA_FIN. Aucun scénario métier lancé pour ce changement ; les neuf scénarios modificatifs restent indisponibles. Le vérificateur continue d'exiger le catalogue SQL réel du processus, sans assimiler le titre à une preuve.

Solution .NET compilée sans avertissement ni erreur, frontend compilé, API rechargée avec le catalogue actualisé. Les observations et rapports précédents sur FIN_ALTEAL restent historiques.

[ASP.NET Core : réponses et SSE](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0), [Vite : build](https://vite.dev/guide/build), [FlaUI 5 : Application et Dispose](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/Application.cs).
