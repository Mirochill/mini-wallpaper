# Mini Wallpaper

Petit prototype Windows pour remplacer une partie utile de Lively Wallpaper sans embarquer une grosse pile applicative.

## Objectif du MVP

- afficher un fond animé local derrière les icônes du bureau ;
- afficher la vidéo via la pile média native de Windows ;
- démarrer avec un fichier vidéo local simple (`Documents/Gifs/wallpaper.mp4` si présent) ;
- mettre automatiquement la vidéo en pause quand une autre fenêtre passe en plein écran.

## Fonctionnement actuel

Au premier lancement :

1. l’app cherche `Documents/Gifs/wallpaper.mp4` ;
2. si ce fichier n’existe pas, elle ouvre un sélecteur de fichier ;
3. elle enregistre la configuration dans `%LOCALAPPDATA%/MiniWallpaper/wallpaper.txt`.

Formats visés dans ce premier jet : `mp4`, `wmv`, `avi`, `mov`.

Un menu dans la zone de notification permet ensuite de :

- choisir un autre fond ;
- mettre en pause ou reprendre ;
- activer ou désactiver le lancement avec Windows ;
- quitter l’application.

## Build

Le code source est dans `native-wpf/`.

```powershell
powershell -ExecutionPolicy Bypass -File .\native-wpf\build.ps1
.\dist-native\mini_wallpaper_native.exe
```

La version finale n’embarque ni navigateur, ni lecteur vidéo tiers.

## État actuel

- la mini-app est installée dans `%LOCALAPPDATA%/Programs/MiniWallpaper/mini_wallpaper.exe` ;
- le lancement automatique avec Windows est activé ;
- Lively Wallpaper a été désinstallé.
