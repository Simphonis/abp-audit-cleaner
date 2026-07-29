# Simphonis.Abp.AuditLogs

Cette bibliothèque contient des outils dont l'objectif est de faciliter ou automatiser le nettoyage du log d'audit ABP.

# Utilisation

La bibliothèque contient module ABP, qui peut être ajouté dans n'importe quelle solution.
En l'ajoutant, un nouveau worker va être enregistré, pour une exécution toutes les heures (non configurable pour le moment). Lorsque ce worker s'exécute, il supprime toutes les entrées du log d'audit dont l'âge dépasse une valeur configurée, qui est d'un an par défaut.
Il va également rendre disponible un nouveau service, `IAuditLogCleaner`, qui fournit une méthode utilitaire pour nettoyer manuellement les logs d'audit.

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
Un certain nombre de réglages sont disponibles sous la forme de Settings ABP. Ils influencent le comportement de la tâche planifiée et n'ont pas d'impact sur l'utilisation directe de `IAuditLogCleaner`.

Vous pouvez modifier la valeur par défaut de ces réglages dans la configuration de l'application. Par exemple :
```js
{
  // ...
  "Settings" : {
    "Simphonis.AuditLogs.AuditLogRetentionPeriod": "100"
  },
  // ...
}

Si vous préférez influer sur la valeur coté C#, le nom des Setting sont dans des constantes de la classe `SimphonisAuditLogSettings`.

**Age maximal des entrées**
Cette valeur est stockée dans le Setting ABP `Simphonis.AuditLogs.AuditLogRetentionPeriod`, et contient l'âge maximal du log d'audit, en jours. La valeur par défaut est de 365 jours.
La tâche planifiée va tenter de supprimer toutes les entrées du log d'audit plus vieux que cette durée configurée.

**Quantité maximale de lignes supprimables**
Cette valeur est stockée dans le Setting ABP `Simphonis.AuditLogs.MaxDeleteCount`, et contient la quantité maximale de lignes à supprimer à chaque passage de la tâche planifiée. Par défaut, cette limite est de 10000 lignes. Sa valeur ne peut pas être supérieure à 1000000.
Ce réglage est présent (et obligatoire !) pour éviter de supprimer trop de données d'un coup, ce qui poserait des problèmes à l'application :
- Blocage de l'application tant que la suppression n'est pas terminée
- En fonction de la configuration du serveur de base de données, la suppression pourrait faire grossir les fichiers de données de manière incontrôlée et saturer l'espace disque.
