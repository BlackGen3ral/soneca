# Desligamento pela internet

O Worker recebe apenas pedidos de desligamento. O PC atualiza sua presença a cada dez segundos e o banco entrega cada pedido uma vez. Pedidos e presença expiram em 30 segundos. O Windows confere a assinatura, o vínculo ativo e a data do pedido antes de executar. O serviço não recebe a credencial do pareamento local nem a chave usada para assinar.

Para publicar em outra conta Cloudflare, aplique `schema.sql` em um banco D1 separado e publique `worker.mjs` com esse banco no binding `DB`. Use o mesmo endereço HTTPS em `InternetShutdown.cs` e no cliente Android `InternetPowerClient.java`.
