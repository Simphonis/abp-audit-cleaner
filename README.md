# Simphonis.Abp.AuditLogs

Cette bibliothèque contient des outils dont l'objectif est de faciliter ou automatiser le nettoyage du log d'audit ABP.

# Utilisation

La bibliothèque contient module ABP, qui peut être ajouté dans n'importe quelle solution.
En l'ajoutant, un nouveau worker va être enregistré, pour une exécution toutes les heures (non configurable pour le moment). Lorsque ce worker s'exécute, il supprime toutes les entrées du log d'audit dont l'âge dépasse une valeur configurée, qui est d'un an par défaut.
Il va également rendre disponible un nouveau service, `IAuditLogCleaner`, qui fournit une méthode utilitaire pour nettoyer manuellement les logs d'audit.

> ⚠️ **Attention** ⚠️ : Ne mettez JAMAIS en place ce worker sur une application contenant déjà beaucoup de données à supprimer. La suppression des lignes va bloquer votre application tant qu'elle n'est pas terminée, et la suppression de plusieurs millions de lignes peut prendre BEAUCOUP de temps.
> De plus, en fonction de la configuration de la base de données, elle peut faire exploser la taille des fichiers de données du serveur.
> Si vous avez déjà de nombreuses lignes à supprimer, faites un premier nettoyage manuel avec l'assistance d'un administrateur de la base de données avant de mettre en place le batch.

## Installation du module

1. Si ce n'est pas déjà fait, [suivez le wiki de ce projet DevOps](https://dev.azure.com/orkeis-proj1/Simphonis.Libs/_wiki/wikis/Simphonis.Libs.wiki/206/Configurer-une-application-.NET-pour-utiliser-le-serveur-Nuget) pour ajouter sa source de packages NuGet à votre projet.
1. Ajoutez le package NuGet `Simphonis.Abp.AuditLogs` à votre solution. On recommande de l'ajouter dans la couche Application. Et si vous en avez plusieurs, utilisez-en une seule pour que le worker ne s'exécute pas en double !
1. Dans le module du projet où vous avez ajouté la bibliothèque, n'oubliez pas d'ajouter une référence au module :

```cs
[DependsOn(typeof(SimphoAuditLogsModule))]
public class MonModule
{
  // ...
  // Pas de modification nécessaire ici !
}
```

## Configuration
Pour le moment, la seule chose configurable est l'âge maximum des messages de log à conserver. Cette valeur est stockée dans le Setting ABP `Simphonis.AuditLogs.AuditLogRetentionPeriod`, et contient l'âge maximal du log d'audit, en jours. La valeur par défaut est de 365 jours.

Vous pouvez modifier la valeur par défaut de ce réglage dans la configuration de l'application. Par exemple :
```js
{
  // ...
  "Settings" : {
    "Simphonis.AuditLogs.AuditLogRetentionPeriod": "100"
  },
  // ...
}

Si vous préférez influer sur la valeur coté C#, le nom du Setting est dans la constante `SimphonisAuditLogSettings.AuditLogRetentionPeriod`.
