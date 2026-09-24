## Изучите [README.md](.\README.md) файл и структуру проекта.

# Задание 1

## C4 Диаграммы

![](architecture/C4_Context.png)
![](architecture/C4_Container.png)

## ER диаграмма
![](architecture/ER_Diagram.png)

# Задание 2

### 1. Proxy

- Реализован сервис на .NET8 C# (./src/microservices/proxy)
- Сервис прокси принимает запросы на /api/movies и перенаправляет их в монолит или в сервис movies, в зависимости от значения переменной окружения GRADUAL_MIGRATION и MOVIES_MIGRATION_PERCENT.
- Сервис запускается на порту 8000 и имеет health-check на /health

- Протестирован постепенный переход, меняя переменную окружения MOVIES_MIGRATION_PERCENT в файле docker-compose.yml.

### 2. Kafka

- Разработан сервис на .NET8 C# (./src/microservices/events), при вызове которого создаются события User/Payment/Movie и обрабатываются внутри сервиса с записью в лог.
- Сервис запускается на порту 8082 и имеет health-check на /health
- Сервис добавлен в docker-compose

#### Postman Тесты

![](architecture/Postman_test1.png)
![](architecture/Postman_test2.png)
![](architecture/Postman_test3.png)
![](architecture/Postman_test4.png)

#### Состояния топиков Kafka из UI

![](architecture/Kafka_Topics.png)

# Задание 3

### CI/CD

Доработан деплой новых сервисов proxy и events в docker-build-push.yml , 
чтобы api-tests при сборке отрабатывали корректно при отправке 
коммита в ваш репозиторий.

#### Github workflows
![](architecture/github_workflows.png)

### Proxy в Kubernetes

#### Шаг 1

1. Создан Personal Access Token (PAT) с правом read:packages
2. В src/kubernetes/*.yaml отредактирован путь до образов (event-service, monolith, movies-service и proxy-service) 
3. Добавлен секрет src/kubernetes/dockerconfigsecret.yaml в поле из ~/.docker/config.json

#### Шаг 2

  Доработан src/kubernetes/event-service.yaml и src/kubernetes/proxy-service.yaml

  - Необходимо создать Deployment и Service 
  - Доработайте ingress.yaml, чтобы можно было с помощью тестов проверить создание событий
 
  - #### Кластер minikube
  
  Был установлен миникуб на локальную машину, поднят кластер и проверена работа ingress.
  
  1. Создан namespace:
  2. Создан секреты и переменные
  3. Развернута база данных:

![](architecture/db_kube.png)

  4. Развернута Kafka:
  5. Развернут монолит:
  6. Развернуты микросервисы:
  7. Развернут прокси-сервис:

![](architecture/pods_kube.png)

  8. Добавлен ingress
  9. Добавлен 127.0.0.1 cinemaabyss.example.com в /etc/hosts
  10. Установлен minikube tunnel для проброса портов
  11. Результат https://cinemaabyss.example.com/api/movies
  ![](architecture/example_com.png)
  
  12. Запущены тесты из папки tests/postman
  ![](architecture/tests.png)
  
#### Шаг 3
Скриншот вывода при вызове https://cinemaabyss.example.com/api/movies
![](architecture/example_com.png)

Cкриншот вывода event-service после вызова тестов.
![](architecture/event_logs.png)

# Задание 4
Для простоты дальнейшего обновления и развертывания вам как архитектуру необходимо так же реализовать helm-чарты для прокси-сервиса и проверить работу 

Для этого:
1. Перейдите в директорию helm и отредактируйте файл values.yaml

```yaml
# Proxy service configuration
proxyService:
  enabled: true
  image:
    repository: ghcr.io/db-exp/cinemaabysstest/proxy-service
    tag: latest
    pullPolicy: Always
  replicas: 1
  resources:
    limits:
      cpu: 300m
      memory: 256Mi
    requests:
      cpu: 100m
      memory: 128Mi
  service:
    port: 80
    targetPort: 8000
    type: ClusterIP
```

- Вместо ghcr.io/db-exp/cinemaabysstest/proxy-service напишите свой путь до образа для всех сервисов
- для imagePullSecret проставьте свое значение (скопируйте из конфигурации kubernetes)
  ```yaml
  imagePullSecrets:
      dockerconfigjson: ewoJImF1dGhzIjogewoJCSJnaGNyLmlvIjogewoJCQkiYXV0aCI6ICJaR0l0Wlhod09tZG9jRjl2UTJocVZIa3dhMWhKVDIxWmFVZHJOV2hRUW10aFVXbFZSbTVaTjJRMFNYUjRZMWM9IgoJCX0KCX0sCgkiY3JlZHNTdG9yZSI6ICJkZXNrdG9wIiwKCSJjdXJyZW50Q29udGV4dCI6ICJkZXNrdG9wLWxpbnV4IiwKCSJwbHVnaW5zIjogewoJCSIteC1jbGktaGludHMiOiB7CgkJCSJlbmFibGVkIjogInRydWUiCgkJfQoJfSwKCSJmZWF0dXJlcyI6IHsKCQkiaG9va3MiOiAidHJ1ZSIKCX0KfQ==
  ```

2. В папке ./templates/services заполните шаблоны для proxy-service.yaml и events-service.yaml (опирайтесь на свою kubernetes конфигурацию - смысл helm'а сделать шаблоны для быстрого обновления и установки)

```yaml
template:
    metadata:
      labels:
        app: proxy-service
    spec:
      containers:
       Тут ваша конфигурация
```

3. Проверьте установку
Сначала удалим установку руками

```bash
kubectl delete all --all -n cinemaabyss
kubectl delete  namespace cinemaabyss
```

Запустите 
```bash
helm install cinemaabyss .\src\kubernetes\helm --namespace cinemaabyss --create-namespace
```

Если в процессе будет ошибка
```code
[2025-04-08 21:43:38,780] ERROR Fatal error during KafkaServer startup. Prepare to shutdown (kafka.server.KafkaServer)
kafka.common.InconsistentClusterIdException: The Cluster ID OkOjGPrdRimp8nkFohYkCw doesn't match stored clusterId Some(sbkcoiSiQV2h_mQpwy05zQ) in meta.properties. The broker is trying to join the wrong cluster. Configured zookeeper.connect may be wrong.
```

Проверьте развертывание:
```bash
kubectl get pods -n cinemaabyss
minikube tunnel
```

Потом вызовите 
https://cinemaabyss.example.com/api/movies
и приложите скриншот развертывания helm и вывода https://cinemaabyss.example.com/api/movies

## Удаляем все

```bash
kubectl delete all --all -n cinemaabyss
kubectl delete namespace cinemaabyss
```
