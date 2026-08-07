# Synchronisation Airtable → RekordBot

RekordBot peut utiliser une table Airtable comme boîte d'entrée pour les morceaux préparés depuis Spotify/MacroDroid.

## Flux

```text
Spotify partagé
    ↓
MacroDroid
    ↓
Airtable
    ↓
RekordBot --airtable-sync
    ↓
WorkflowImportService
    ↓
Rekordbox
```

La synchronisation ne crée pas un second moteur d'écriture Rekordbox : chaque ligne Airtable est convertie en document Workflow 2.0 puis passée au `WorkflowImportService` existant.

## Prérequis

- Le morceau doit déjà exister dans la bibliothèque Rekordbox et son fichier doit être accessible sur disque.
- Rekordbox doit être fermé pour une synchronisation réelle.
- Le token Airtable doit disposer de `data.records:read` et `data.records:write` sur la base concernée.
- Commencer par un `--dryrun` est recommandé.

## Configuration

Copier `.env.example` vers `.env` et renseigner au minimum :

```dotenv
AIRTABLE_TOKEN=pat...
AIRTABLE_BASE_ID=app...
AIRTABLE_TABLE_ID=tbl...
```

Valeurs optionnelles :

```dotenv
AIRTABLE_VIEW=
AIRTABLE_STATUS_FIELD=Statut
AIRTABLE_PENDING_STATUS=À préparer dans Rekordbox
AIRTABLE_READY_STATUS=Prêt à mixer
```

Le fichier `.env` est chargé automatiquement par `CueGen.Console` et ne doit pas être commité.

## Champs Airtable lus

La synchronisation lit actuellement les champs suivants :

- `Titre`
- `Artiste`
- `Genre Soundcharts`
- `Énergie`
- `Mood`
- `Situation`
- `Lien Spotify`
- `Commentaires`
- `Statut`

Seuls les enregistrements dont `Statut` vaut `AIRTABLE_PENDING_STATUS` sont sélectionnés.

## Correspondance avec le Workflow 2.0

- `Titre` + `Artiste` servent à retrouver de manière déterministe le morceau déjà présent dans Rekordbox.
- Le chemin réellement connu de Rekordbox est ensuite utilisé dans le document d'import.
- `Mood` est converti vers la couleur/étiquette de la taxonomie Workflow.
- `Énergie` devient la note 1–5.
- `Genre Soundcharts` est conservé lorsqu'il existe dans la taxonomie ; sinon RekordBot tente un mapping vers la famille connue (`House`, `Techno`, `Pop`, etc.).
- `Situation` est convertie vers les situations actuellement acceptées par la taxonomie.
- Le statut Workflow est dérivé des informations disponibles : `Mood` → `Energy` → `Tags` → `Hot Cues`.

Les playlists attendues sont calculées par `WorkflowPlaylistPlan`, exactement comme pour un import JSON classique.

## Exécution

Vérification sans aucune écriture :

```powershell
dotnet run --project CueGen.Console -- --airtable-sync --dryrun
```

Synchronisation réelle :

```powershell
dotnet run --project CueGen.Console -- --airtable-sync
```

En mode réel, les protections existantes de RekordBot restent actives : Rekordbox doit être fermé et une sauvegarde vérifiée de `master.db` est créée avant mutation.

Après un import réussi, le statut Airtable est remplacé par `AIRTABLE_READY_STATUS`. En `--dryrun`, Airtable n'est jamais modifié.

## Résolution des morceaux

La résolution privilégie un titre et un artiste normalisés identiques. Si l'artiste Airtable ne correspond pas mais qu'un seul morceau Rekordbox possède ce titre, ce morceau est retenu avec un avertissement. En cas d'ambiguïté, le morceau est ignoré et l'erreur apparaît dans le résultat JSON de la synchronisation.

Cette stratégie évite d'écrire sur un morceau ambigu et laisse `WorkflowImportService` effectuer une seconde vérification sur le chemin, le titre et l'artiste avant toute modification.
