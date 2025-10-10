# GlbOpenTKDemo

Projet .NET 8 (C#) minimal qui charge un fichier **.glb** via **AssimpNet** et l'affiche avec **OpenTK** (OpenGL).
Il bascule automatiquement sur un simple cube si aucun `assets/models/model.glb` n'est présent.

## Prérequis
- .NET 8 SDK
- Drivers GPU à jour (OpenGL 3.3+)
- Les runtimes natifs d'Assimp sont intégrés via les packages `AssimpNet.runtime.*` (Windows, Linux, macOS Intel).
  - Si vous êtes sur Apple Silicon (ARM64), utilisez Rosetta **ou** changez les packages runtime vers une variante compatible quand disponible.

## Installation
```bash
dotnet restore
dotnet run
```

## Utiliser votre propre GLB
- Déposez votre modèle dans `assets/models/model.glb` (ou modifiez le chemin dans `Viewer.cs`).  
- Les textures intégrées (*0, *1, …) sont prises en charge. Les textures externes (cas .gltf) doivent être à côté du modèle.

## Contrôles
- La caméra est statique (lookAt), le modèle tourne lentement pour l'aperçu. Adaptez selon vos besoins.

## Notes
- Les coordonnées GLTF (Y-up, right-handed) sont généralement compatibles. Ajustez la matrice `uModel` si l'orientation ne convient pas.
- Pour un pipeline PBR complet (metal/rough/normal), enrichissez les shaders et le loader (tangentes, etc.).
