# Roadmap RekordBot

Cette roadmap transforme RekordBot d'un fork de CueGen enrichi en un assistant de préparation DJ complet, fiable et automatisé autour du flux Spotify → Airtable → RekordBot → Rekordbox.

## Ordre recommandé

| Priorité | Chantier | Objectif |
|---|---|---|
| P0 | Identifiants stables | Éviter tout faux matching Airtable/Rekordbox |
| P0 | Retour d'état Airtable | Faire d'Airtable le tableau de bord du pipeline |
| P0 | Refactor AirtableSyncService | Séparer API, matching, mapping et orchestration |
| P1 | Tests Airtable et intégration | Couvrir HTTP, pagination, erreurs et dry-run |
| P1 | Découverte des fichiers locaux | Préparer automatiquement les fichiers non encore résolus |
| P1 | Taxonomie configurable | Sortir les alias/mappings du code C# |
| P1 | .NET 10 + CI Windows | Moderniser le runtime et tester l'environnement réel |
| P2 | Commandes `sync` et `status` | Simplifier l'usage quotidien |
| P2 | Branding et releases | Transformer proprement CueGen en RekordBot |

---

## [P0] Utiliser ISRC et Spotify Track ID pour identifier les morceaux

### Objectif
Rendre la correspondance Airtable → Rekordbox déterministe et éviter les faux matchs sur `Titre + Artiste`.

### Travaux
- Ajouter `Spotify Track ID` et `ISRC` au modèle Airtable lu par RekordBot.
- Extraire le Spotify Track ID depuis `Lien Spotify` si nécessaire.
- Introduire un `TrackMatcher` avec la stratégie :
  1. ISRC exact
  2. Spotify Track ID exact
  3. titre + artiste normalisés exacts
  4. sinon aucune écriture automatique
- Supprimer le fallback silencieux sur titre seul en mode écriture.
- Exposer dans le résultat la méthode de matching utilisée.

### Critères d'acceptation
- [ ] Un match ambigu ne modifie jamais `master.db`.
- [ ] ISRC et Spotify ID sont prioritaires sur titre/artiste.
- [ ] Le dry-run indique pourquoi et comment chaque morceau a été matché.
- [ ] Tests couvrant exact match, ambiguïté et absence de match.

---

## [P0] Renvoyer l'état détaillé de RekordBot dans Airtable

### Objectif
Faire d'Airtable le tableau de bord de la préparation des morceaux.

### Travaux
- Ajouter/supporter des champs :
  - `État RekordBot`
  - `Dernière synchro`
  - `Erreur RekordBot`
  - `Fichier local`
  - `Méthode de matching`
  - `Tags appliqués`
  - `Hot cues générés`
- Définir des statuts explicites :
  - `À préparer`
  - `Fichier introuvable`
  - `Match ambigu`
  - `Prêt à importer`
  - `Importé`
  - `Erreur`
- Ne jamais écraser une erreur sans conserver le diagnostic utile.
- Garantir qu'un dry-run ne modifie jamais Airtable.

### Critères d'acceptation
- [ ] Chaque ligne traitée possède un état compréhensible sans consulter les logs console.
- [ ] Les erreurs de matching et d'import sont visibles dans Airtable.
- [ ] Un import réussi stocke la date de dernière synchro.
- [ ] Tests sur les transitions d'état.

---

## [P0] Découper `AirtableSyncService`

### Objectif
Réduire les responsabilités du service actuel et rendre les composants testables sans réflexion sur des méthodes privées.

### Architecture cible
- `AirtableClient` : HTTP Airtable uniquement.
- `TrackMatcher` : correspondance Airtable/Rekordbox.
- `AirtableWorkflowMapper` : mood, énergie, genre, situation → Workflow 2.0.
- `RekordboxTrackCatalog` : lecture de la bibliothèque locale.
- `AirtableSyncService` : orchestration uniquement.

### Travaux
- Extraire les méthodes de mapping dans un composant dédié.
- Injecter les dépendances au lieu de créer `HttpClient`, repository et services directement.
- Introduire des interfaces là où elles apportent un vrai gain de testabilité.
- Supprimer les tests reposant sur `Reflection` pour atteindre des méthodes privées.

### Critères d'acceptation
- [ ] `AirtableSyncService` ne contient plus de logique HTTP ni de règles de mapping.
- [ ] Les mappings sont testés via API publique.
- [ ] Les composants peuvent être testés sans accès réseau ni vraie base Rekordbox.

---

## [P1] Ajouter des tests HTTP Airtable et des tests d'intégration

### Objectif
Tester le protocole Airtable réel et les protections du pipeline.

### Travaux
- Injecter un `HttpClient`/`HttpMessageHandler` testable.
- Tester :
  - pagination via `offset`
  - `filterByFormula`
  - PATCH par lots
  - 401/403
  - 404
  - 429 rate limit
  - 500
  - JSON invalide
- Vérifier qu'aucun PATCH Airtable n'est envoyé en `--dryrun`.
- Ajouter un test end-to-end sur une copie de `test.db` avec faux Airtable.
- Vérifier l'idempotence d'une synchronisation répétée.

### Critères d'acceptation
- [ ] Les erreurs HTTP produisent un diagnostic exploitable.
- [ ] Le retry éventuel respecte les limites Airtable et reste borné.
- [ ] Le dry-run est garanti sans écriture réseau Airtable et sans mutation DB.
- [ ] La CI exécute ces tests.

---

## [P1] Découvrir et rapprocher automatiquement les fichiers audio locaux

### Objectif
Ne plus exiger que tout soit déjà parfaitement résolu dans Rekordbox avant la synchronisation.

### Travaux
- Ajouter une configuration de dossiers musicaux à scanner.
- Lire les tags standards des fichiers audio : titre, artiste, ISRC et autres identifiants disponibles.
- Construire un index local des fichiers.
- Réutiliser la même stratégie de `TrackMatcher` que pour Rekordbox.
- Retourner les états : `fichier trouvé`, `fichier absent`, `plusieurs fichiers possibles`.
- Ne jamais télécharger de contenu depuis Spotify.

### Critères d'acceptation
- [ ] Un fichier local avec ISRC exact est retrouvé même si son nom de fichier diffère du titre.
- [ ] Les doublons restent en attente d'une décision explicite.
- [ ] Le scan est suffisamment rapide pour une bibliothèque DJ courante grâce à un index/cache.

---

## [P1] Déplacer les alias et mappings dans la taxonomie JSON

### Objectif
Permettre d'ajuster les genres, moods et situations sans recompiler RekordBot.

### Exemple cible
```json
{
  "genre_aliases": {
    "Afro Tech": ["Afro House", "Techno"],
    "Organic House": ["Organic House", "House"]
  },
  "situation_aliases": {
    "Festival": "Main Floor",
    "Peak-time": "Peak Time"
  },
  "mood_aliases": {
    "Énergique": "Énergie"
  }
}
```

### Travaux
- Étendre le schéma de `workflow_taxonomy_v2.json`.
- Déplacer les dictionnaires codés en C# vers le JSON.
- Valider au chargement que les valeurs cibles existent réellement.
- Conserver un comportement déterministe si un alias est inconnu.

### Critères d'acceptation
- [ ] Ajouter un alias ne nécessite plus de modification C#.
- [ ] Une taxonomie invalide échoue au démarrage avec une erreur précise.
- [ ] Tests de validation et de mapping.

---

## [P1] Migrer le CLI vers .NET 10 et ajouter une CI Windows

### Objectif
Sortir de .NET 6 et tester RekordBot dans un environnement proche de l'utilisation réelle.

### Travaux
- Migrer `CueGen.Console` et `CueGen.Test` vers `net10.0`.
- Vérifier la compatibilité des dépendances SQLCipher/SQLite/TagLib.
- Corriger les warnings nullable existants.
- Ajouter une matrice CI :
  - `ubuntu-latest`
  - `windows-latest`
- Exécuter restore/build/test sur les deux plateformes.
- Ajouter un test spécifique des chemins Windows.

### Critères d'acceptation
- [ ] Build sans erreur sur Linux et Windows.
- [ ] Tous les tests passent sur les deux runners.
- [ ] Aucun warning lié à un framework EOL.

---

## [P2] Introduire les commandes `rekordbot sync` et `rekordbot status`

### Objectif
Réduire l'usage quotidien à quelques commandes compréhensibles.

### Commandes cibles
```text
rekordbot sync --dry-run
rekordbot sync
rekordbot status
```

### `sync`
- charge Airtable
- résout les morceaux
- applique le mapping Workflow
- importe dans Rekordbox
- met Airtable à jour
- fournit un résumé final

### `status`
Afficher par exemple :
```text
23 morceaux Airtable
18 prêts à synchroniser
3 fichiers manquants
1 genre non mappé
1 morceau ambigu
```

### Critères d'acceptation
- [ ] Les anciennes options restent compatibles pendant une période de transition.
- [ ] Le code retour CLI est non nul si au moins une erreur bloquante survient.
- [ ] Une sortie JSON optionnelle est disponible pour les scripts.

---

## [P2] Renommer proprement CueGen en RekordBot et automatiser les releases

### Objectif
Faire du dépôt un projet autonome clairement identifiable.

### Travaux
- Mettre à jour :
  - README
  - nom/description des assemblies
  - `PackageId`
  - URLs du dépôt
  - métadonnées NuGet éventuelles
  - textes CLI
- Conserver l'attribution et la licence du projet CueGen d'origine.
- Ajouter un workflow de release qui produit un exécutable Windows autonome.
- Publier l'archive comme GitHub Release.
- Ajouter une version RekordBot propre (`0.x` puis `1.0`).

### Critères d'acceptation
- [ ] L'utilisateur peut télécharger une release et lancer RekordBot sans `dotnet run`.
- [ ] Les références à l'ancien dépôt CueGen ne subsistent que dans la section crédits/historique.
- [ ] La release est construite automatiquement à partir d'un tag.

---

## Définition de "v1 RekordBot"

La v1 peut être considérée atteinte lorsque :

- [ ] le matching des morceaux utilise des identifiants stables ;
- [ ] Airtable donne l'état complet de chaque morceau ;
- [ ] le pipeline est couvert par des tests réseau et DB ;
- [ ] le projet tourne sur .NET 10 et CI Windows ;
- [ ] `rekordbot sync` orchestre le flux complet ;
- [ ] une release Windows autonome est publiée ;
- [ ] aucun match ambigu ne peut modifier automatiquement le mauvais morceau.
