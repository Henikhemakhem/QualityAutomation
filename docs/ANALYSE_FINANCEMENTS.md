# Analyse du legacy Financements

Document repris et vérifié pour l'application web Quality Automation. Complément ci-dessous : raccourcis et mesures du second passage ; les sections historiques conservent leurs observations datées.

## Complément pour l'application web (9 octobre 2026)

- Les rôles de `CDTEprtView`, `CLEmpruntUI`, `CEpOpRevTauxDlg`, `CEpOpMouvDlg`, `CGestBigListeEmpt::DoSearch` et `CGestAppliContext::OpenInternal` ont été revérifiés dans les sources avant l'ajout de l'API/React.
- `Main/LoanPlus/res1.rc:8496`, `IDR_MAINFRAME ACCELERATORS` : Ctrl+1 → `ID_KEY_CTRL1`, gestionnaire `CMainFrame::OnKeyCtrl1` (`MainFrm.cpp:684`), navigation de focus liée au navigateur. Ce n'est pas une commande prouvée d'ouverture directe de l'emprunt.
- `res1.rc:8501`, `IDR_DTEMPT ACCELERATORS` : Ctrl+Tab → `ID_KEY_CTRLTAB`, `CDTEmptView::OnKeyCtrltab` (`DTEmpt.cpp:1390`), rotation des onglets de la couche historique. Ne pas l'assimiler automatiquement au conteneur ATL courant.
- `DTBigListe.cpp:501` : Entrée quand le champ ID_SEARCH_FIELD a le focus appelle la recherche. C'est un mécanisme confirmé statiquement, pas un test de saisie effectué.
- Les textes de menu comportant Ctrl+P/Ctrl+Z ne sont pas une preuve d'un raccourci d'ouverture ou de sauvegarde applicable à la fiche actuelle. Aucun raccourci « ouvrir emprunt 2261 » n'est présumé.
- L'exécutable installé expose la fenêtre `Financements 25.1`, classe `Loan2000Class`. UIA2 a observé le dialogue `Base incompatible`, classe `#32770`, bouton OK AutomationId **2**, texte AutomationId **65535**. Ne pas généraliser cet ID OK aux dialogues métier, dont la ressource source IDOK vaut 1.
- Message réel : base affichée FIN_ALTEAL, puis lors d'un autre essai QA_FIN, en version **26.10.0** alors que l'exécutable attend **25.13**. Ce message ne prouve pas à lui seul le catalogue SQL physique actif, mais il établit l'incompatibilité rencontrée. Aucune migration/restauration ni validation du dialogue par le robot.
- Un autre essai a observé la modale `Connexion à une base` avec sélection de base DB Manager. Son contenu n'a pas été utilisé pour se connecter automatiquement.
- Le dossier `QualityAutomation/results/diagnostic-uia2` contient l'arbre réel observé ; le rapport `results/0ddc3aeb-3a53-4101-89f4-2b27bc7bf78b` confirme le blocage avant la navigation. Fichiers locaux exclus de Git.

La logique de lancement du nouvel agent détecte la classe principale réellement observée et les modales bloquantes. Les sélecteurs de la liste et de la fiche restent à mesurer sur une installation compatible connectée à QA_LEGACY. La recherche/ouverture n'est pas annoncée implémentée sur la seule base de l'analyse source.

Analyse statique du dépôt, 9 octobre 2026. Aucun fichier C++ modifié, aucune écriture SQL et aucune opération métier exécutée. Les identifiants ci-dessous sont des **IDs de ressources Win32**, pas des AutomationId FlaUI observés.

## Entrée et initialisation

- Solution : `Main/ToutLoan.sln` ; exécutable : projet `Main/LoanPlus/LoanPlus.vcxproj`.
- `CLoanPlusApp : CWinAppPlus`, `Main/LoanPlus/LoanPlus.h:42` ; entrée `CLoanPlusApp::InitInstance`, `LoanPlus.cpp:317`.
- Initialisation : registre Salvia, `ParseCommandLine`, `PrepareApplication`, création de `CMainFrame`, connexion via le contexte, titre et accueil. La connexion peut déclencher vérification de version, synchronisation et imports : lancer l'application n'est pas nécessairement une opération sans effet sur la base.
- `Main/LoanPlus/LoanPlus.cpp:717` enregistre le document/vue actuel `LDT_EPRT` avec `CDTEprtDoc`, `CDTEprtMDIChildWnd`, `CDTEprtView`.

## Connexion et choix de base

- `Main/LoanPlus/AppliContext.cpp:630` : `CGestAppliContext::OpenInternal` utilise `CDbmService::GetConnection`, la dernière base `INI_STRING_LASTBASE` et la base automatique du contexte.
- `Main/LoanShare/Dbm.cpp:329` : `CDbmService::GetConnection` délègue à `DbmUtil::Connect` puis construit la chaîne par `IBase.MakeConnectionString`.
- `Main/Financements.Core/Util/DbmUtil.cs:130` : liste des bases autorisées, recherche de `baseAuto`, sinon boîte externe `ChooseIBase`, permission, login/session, construction et vérification de connexion, variante et licence. Le dialogue de sélection appartient à la dépendance DB Manager : sa ressource n'est pas identifiée dans ce dépôt.
- `DbmUtil.cs:59`, `ToDbInfo` distingue `Label = b.Name`, `Base = b.Base`, `Server = b.Server`, `IntegratedSecurity = b.NTSecurity`.
- `Main/LoanPlus/AppliContextLoanDette.cpp:87` conserve `m_sBaseName = pIBase->Name` et l'ActiveConnection du contexte. `MainFrm.cpp:156` et `LoanPlus.cpp` affichent ce **libellé** dans le titre : il ne prouve pas le catalogue SQL physique.
- Configuration : `LoanPlus.cpp:581`, `[Config] DirectConnectionDlg` ; `LoanPlus.cpp:668`, `[Config] LastBase`, mémorisée après sélection. Les bases/permissions proviennent de DB Manager, pas d'une simple constante dans `Financements.Config`.
- `CLoanCommandLineInfo::ParseParam`, `LoanPlus.cpp:1050` : `-USER:`, `-BASE:`, `-PASS:`. Analyse fragile par positions dans la ligne complète mise en majuscules ; ne pas supposer que `-BASE:QA_LEGACY` seul suffit. Ne pas mettre de secret dans les logs ou lignes de commandes du pilote.
- `Main/LoanPlus/MainFrm.cpp:896`, `OnDatabaseChanged`, permet de changer de base en mode Shiva. Une vérification au lancement ne suffit donc pas avant sauvegarde.

### Contrainte de sécurité à appliquer

La cible donnée par l'utilisateur est `localhost\SQLEXPRESS`, catalogue `FIN_ALTEAL`. Elle n'est pas `QA_LEGACY`. Aucune modification n'est autorisée sur cette cible dans le périmètre de cette mission. Il faut une base QA_LEGACY isolée, une entrée DB Manager dont le serveur/catalogue sont vérifiés, puis une preuve de la connexion active avant chaque modification. Un titre, une configuration du robot ou une connexion SQL indépendante ne prouvent pas à eux seuls la connexion active du processus Financements.

## Fiche emprunt : chemin réellement utilisé

`Main/LoanPlus/CenterAction.cpp:317`, `CCenterAction::DoEprtOuvrir` :

1. `CDTEprtInfo::Load` (`DTEprt.cpp:134`) charge `IEmprunt.Load(m_nEmpt)` et vérifie `Nempt`. Il gère consultation, simulation, verrouillage et fiche inexistante.
2. Création COM `LEmpruntUI`, affectation de la fiche, `LocalContext->Activate(LDT_EPRT, ...)`.
3. Conteneur MFC `CDTEprtView` ; page ATL `CLEmpruntUI` dans `Main/LoanDetteUI/LEmpruntUI.h:57`, héritant de `CDialogImpl`, `IPropertyPagePlusImpl` et `IFicheUIImpl`.
4. Ressource `IDD_LEMPRUNTUI = 201`, `LoanDetteUI.rc:105` ; onglets `IDC_TAB = 201`, `OnSelchangeTab` / `OnSelchangingTab`.
5. Sous-pages : identification (`EpIdentUI`, `EpRefUI`), caractéristiques (`EpCaracUI`), tableau (`CEpTableauUI`, `CEpTabAmtUI`), options et autres pages métier.

Autre couche historique : `Main/LoanPlus/VEmprunt.h`, `CVEmprunt`, `DTEmpt.cpp`, `CDTEmptDoc` / `CDTEmptView`, `EmptPages.*`. Les dialogues `CEmptTransRevTaux1Dlg/2Dlg/3Dlg` et `CEmptTransRembAntDlg` de `EmptTransDlg.*` ne doivent pas être confondus avec les dialogues ATL du chemin `LDT_EPRT`. Leur présence ne démontre pas leur utilisation par l'installation courante.

## Recherche et ouverture

- Liste : `Main/LoanPlus/DTBigListe.cpp:172`, champ toolbar `ID_SEARCH_FIELD = 32832` (`res1.h:791`). Entrée déclenche `CDTBigListeMDIChildWnd::PreTranslateMessage` puis `CDTBigListeView::CallSearch`, lignes 501 et 1385.
- `Main/LoanPlus/BigListe.cpp:1942`, `CGestBigListeEmpt::DoSearch` : filtre composite **OU** entre numéro (`d_dette.nempt` si numérique), contrat, programme et sous-programme. « 2261 » peut renvoyer plusieurs emprunts : ne jamais ouvrir aveuglément la première ligne.
- `DTBigListe.cpp:1149`, double-clic : lit le numéro en colonne 1 et appelle `DoAction(ACTION_DEFAULT)`.
- `BigListe.cpp:488`, `CGestBigListeEmpt::DoAction` → `Ouvrir` → action centrale d'ouverture. Contrôler le numéro affiché dans la fiche, `IDC_NEMPT = 1011` (`LoanDetteUI/resource.h:294`), sous réserve de sa visibilité réelle.
- La grille utilise les composants Spread/APIGrid : accessibilité des lignes et cellules à mesurer. Le texte des nœuds d'accès à la liste doit être observé sur l'installation, pas inventé.

## Révision de taux

Chemin ATL : `Main/LoanDetteUI/EpTableauUI.h:86`, bouton `IDC_OPERATION = 1032` → `CEpTableauUI::OnClickedOperation` → `OnOperation` (`EpTableauUI.cpp:587`). L'échéance sélectionnée initialise la date ; vérification des protections et confirmation possible si opération antérieure à la dernière opération. Le popup est construit par `CEpOperations::CreateMenu` (`EpOperations.cpp:76`). Libellé source : **Révision de taux**.

- `CEpOpRevTaux::Do` (`EpOperations.cpp:199`) ouvre `CEpOpRevTauxDlg` ou `CEpOpRevTauxStructDlg` selon l'emprunt.
- `CEpOpRevTauxDlg::GetIDD` (`EpOpDlgs.cpp:14`) sélectionne les ressources selon `GetModeRevi` (`EpOperations.cpp:388`).

| Ressource ATL | ID | Particularité |
| --- | ---: | --- |
| IDD_EPOP_REVTAUX_SIMPLEDLG | 4024 | Date effet, nouveau taux |
| IDD_EPOP_REVTAUX_PROGFORCEDLG | 4031 | Progression optionnelle et case forcer |
| IDD_EPOP_REVTAUX_PROGDLG | 4032 | **Variation de taux**, progression : pas automatiquement un taux absolu |
| IDD_EPOP_REVTAUX_TAUXSTRUCTDLG | 4114 | Indice/taux structuré ; parcours spécialisé |

`LoanDetteUI.rc:948` et suivants ; titre **Révision de taux**. Contrôles : `IDC_REVTAUX_DATE = 4663`, `IDC_REVTAUX_TAUX = 4664`, plus `IDC_REVTAUX_PROGRESSION`, `IDC_FORCERPROGRESSION` selon variante. `IDOK = 1`, `IDCANCEL = 2` sont locaux au dialogue.

`EpOpDlgs.cpp:33` sous-classe les edits numériques et date ; `OnOK:84` ne ferme qu'après `VerifyCtrls`. `DoRevTaux:337` délègue au moteur `CLRevisionTaux` dans `Main/LTransfo/LRevisionTaux.cpp:60`. Ce moteur appelle les calculs du tableau existants ; le robot doit exclusivement déclencher ce chemin par l'UI.

**Vérification du résultat** : `EpTableauUI.cpp` ne marque la fiche modifiée et ne rafraîchit qu'en cas `EpOpResult_Ok`. Un dialogue fermé ne prouve pas le succès. Les échéances protégées, les messages de refus, « aucune action » et la variante variation doivent être distingués. L'historique est ajouté avant certains contrôles dans `CEpOpRevTaux::Do` : une ligne d'historique seule ne garantit pas que le calcul a abouti.

## Remboursement anticipé

- Même popup Opération ; `CEpOpRembAnti` (`EpOperations.cpp:575`), `Do:582`.
- Dialogue **CEpOpMouvDlg** (`EpOpDlgs.h:285`), `MODE_REMBANT`, et non la seule page de conditions `CEpRembAntiUI`.
- Ressource `IDD_EPOP_MOUVDLG = 4043`, `LoanDetteUI.rc:909`, titre **Remboursement anticipé** ; dialogue partagé avec transfert et diminution, titre/visibilité adaptés au mode.
- `IDC_REMBANT_TYPE = 4684` (ComboBoxEx32), `IDC_REMBANT_DATE = 4681`, `IDC_REMBANT_MONTANT = 4682`, numéro refinancement, pénalités, motif et ventilation.
- `EpOpDlgs.cpp:601`, initialisation des contrôles ; `OnOK:725`, récupération/validation ; `EpOperations.cpp:598` renseigne DateRA/MontantRA/NemptRefin/Option et appelle `LRemboursementAnticipe.ApplyTo`.
- `Main/LoanDetteUI/EpRembAntiUI.*` et `EpRembAntisUI.*` sont des pages de données/conditions. Ressources 4110/4130 : elles ne remplacent pas le dialogue d'opération.

## Validation et sauvegarde

- Dialogue métier : `IDOK`, validation locale puis application de l'opération en mémoire.
- Fiche : `Main/LoanPlus/DTEprt.cpp:617/631`, `IDC_ENREGISTRER = 1031` (`res1.h:228`), `CDTEprtView::OnEnregistrer` → `Enregistrer:841` → `CanValid:996` et `GetIFicheUI()->Save:889`.
- `SetModified:990` active/désactive le bouton selon l'état modifié. Vérifier bouton actif après opération, puis état enregistré, absence de dialogue d'erreur et relecture du tableau après réouverture. Le clic seul et l'absence d'exception FlaUI ne suffisent pas.
- Ancien parcours `DTEmpt.cpp:1166`, `CDTEmptView::Enregistrer` → `CDTEmptDoc::Enregistrer:339`.

## Export amortissement

- `CEpTableauUI::OnClickedExport`, `EpTableauUI.cpp:558`, bouton owner-draw `IDC_EXPORT = 1035` ; délègue `IActionUI.Do(AUI_EXPORT)`.
- `Main/LoanDetteUI/EpTabAmtUI.cpp:1143` → `ApiGridClient::SaveGrid(..., L"TabAmort", true)`.
- `Main/LoanDetteUI/AtlApiGridClient.h:39` : export grille avec ancien format Excel demandé. Vérifier dialogue fichier, extension/format effectif et fichier non vide sur le poste. Aucun export réel effectué.

## Conclusion de l'étape A et passage à B

Classes et ressources identifiées dans les sources ; aucune interaction utilisateur testée à ce stade. SDK installé : .NET 10.0.401. Processus `Financements.exe` trouvé dans `C:\Program Files (x86)\Salvia\Financements`, sans titre principal lors de l'inventaire PowerShell. La version binaire installée n'est pas démontrée identique au dépôt.

Prochaine étape : sonde FlaUI UIA3/UIA2 sans modification pour inventorier les fenêtres, AutomationId, types, patterns et contrôles personnalisés. Bloquer le scénario 2261 jusqu'à preuve de connexion physique QA_LEGACY. Aucun sélecteur de production ne doit être déduit uniquement des IDs Win32.
