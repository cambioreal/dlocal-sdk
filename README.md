# dlocal-sdk

Cliente .NET tipado (`CambioReal.Dlocal.Client`) para a **dLocal Payins API v2.1** — payment
methods, consulta/criação/cancelamento de payins (PIX/boleto/cartão/TED), cotação de câmbio,
reembolsos e chargebacks, com assinatura **V2-HMAC-SHA256** (`login+date+body`) e **credenciais
por produto/MID** (checkout/card/remittance/payout — a dLocal emite um trio por produto; toda
operação declara o produto).

Validação viva (2026-07-15): probes de leitura POR PRODUTO executados nos 4 MIDs
(payments-methods 200 com dados reais) + 404 de domínio code 4000 (payments e refunds) + cotação
real de câmbio (`GET /currency-exchanges?from=USD&to=BRL`). `CreatePaymentAsync`/
`CreateRefundAsync`/`CancelPaymentAsync` = **write** (os dois primeiros movem fundos, o terceiro
muta estado de um payment existente), nunca executados (goal §0.5). Cashout/payout (API legada
separada) e webhooks fora do v1 por decisão registrada; `secure_payments` (PAN/CVV cru) e
cancelamento de token de wallet deliberadamente fora deste incremento.

0.3.0 (2026-07-16): `POST /payments/{id}/cancel` (cancela autorização de cartão não capturada OU
payment pendente de APM — mesmo path oficial para os dois) e `GET /chargebacks/{id}` (a dLocal
não documenta endpoint de listagem). 13 unit verdes; teste sandbox de chargeback escrito
(`[Trait("Category","Sandbox")]`) mas não executado nesta sessão (sem acesso a `pass`). Descoberta
nesta versão: `status_code` vem como string nos exemplos oficiais de cancelamento/chargeback
(diferente do número em refunds) — SDK agora tolera as duas formas
(`JsonNumberHandling.AllowReadingFromString`).

Secrets: `pass cambio-real-v2/dlocal/demo-env`. Discovery: `docs/providers/dlocal/discovery.md`.
