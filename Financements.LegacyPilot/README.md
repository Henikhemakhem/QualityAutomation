# Financements.LegacyPilot

Ce projet est une bibliothèque de classes référencée par Financements.QualityApi. L'API appelle DesktopRunner.Execute sur un thread STA dédié, avec configuration figée, scénario, emprunt, jeton d'annulation et callback de progression. Le pilote conserve les formats de rapport et de logs. La connexion physique QA_FIN et l'ouverture de « Liste complète » sont implémentées ; la recherche et la fiche restent à valider.

DesktopDiagnostics.Inspect conserve l'inventaire UIA en lecture seule. Son appelant doit fournir un thread STA, un PID Financements et un dossier de sortie. La bibliothèque n'a plus de commande dotnet run et aucun diagnostic n'est exposé par HTTP. Un appel COM bloqué ne peut pas être interrompu de force dans le processus API.

Compiler depuis la racine du dépôt :

    dotnet build QualityAutomation/backend/Financements.QualityApi/Financements.QualityApi.csproj

Voir le README principal, ANALYSE_LEGACY.md et FAISABILITE_FLAUI.md pour le contexte et les observations historiques.
## Sécurité et état du pilote

La demande actuelle autorise **FIN_ALTEAL** sur `localhost\SQLEXPRESS` pour la navigation en lecture seule. DesktopRunner vérifie le catalogue physique par SQLCMD et `sys.dm_exec_sessions`, en liant les sessions au PID Financements et au nom du poste. Le titre de fenêtre seul ne constitue pas une preuve. Le robot ne choisit aucune base ni ne renseigne le dialogue Login ; les actions modificatives restent indisponibles. La navigation liste/fiche reste à implémenter.

`scenarios/revision-taux-fixe.json` contient le scénario demandé, comme **donnée de catalogue non exécutable dans cette version**. Les actions futures sont ouvrir_emprunt, revision_taux, remboursement_anticipe, diminution, lissage, report_echeances, revision_indice, sauvegarder, export_tableau_amortissement. Aucune action non testée n'est simulée.

## Architecture cible après la faisabilité

| Composant | État / responsabilité |
| --- | --- |
| FinancementsLauncher / DesktopRunner | Attachement par PID de la sonde ; lancement/attachement supervisé, session interactive, mutex et garde-fous |
| AccessibilityInspector | Inventaire UIA3/UIA2 implémenté et exécuté |
| ExecutionResult, StructuredLog, ScreenshotService | Implémentés ; rapports/logs/captures produits |
| LoginPage | À implémenter après observation du dialogue DB Manager et preuve de connexion active |
| EmpruntPage | À implémenter après observation de la liste, de la grille et de l'identifiant fiche |
| RevisionTauxPage | À implémenter après mesure des variantes et preuve du résultat dans le tableau |
| RemboursementAnticipePage | À implémenter après mesure de CEpOpMouvDlg |
| DesktopRunner / RunCoordinator | Catalogue YAML serveur, orchestration du lancement et arrêt sur préconditions ; actions métier non implémentées refusées |

Le moteur devra refuser un scénario vide/malformé, les paramètres inconnus et toute action non implémentée avant lancement. Avant chaque mutation, valider à nouveau base, numéro de fiche, fenêtre attendue et absence de modale inconnue. Ne jamais assimiler fermeture d'un dialogue à réussite métier. Ajouter les délais configurables, vérification par relecture après sauvegarde et exports seulement une fois ces observations obtenues.

## Validation de cette livraison

Build réussi (0 avertissement, 0 erreur). Inspections UIA3/UIA2 réussies avec accès au bureau ; lancement/attachement et rapports de blocage testés via l'API. Aucun test de saisie, de recherche, de révision de taux, de remboursement, de sauvegarde ou d'export réalisé. L'installation 25.1 attend une base 25.13, alors que les dialogues observés indiquent 26.10.0 : voir [les essais détaillés](../docs/VALIDATION.md).
