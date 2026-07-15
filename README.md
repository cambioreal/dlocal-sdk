# dlocal-sdk

Cliente .NET tipado (`CambioReal.Dlocal.Client`) para a **dLocal Payins API v2.1** — payment
methods, consulta e criação de payins (PIX/boleto/cartão/TED), cotação de câmbio e reembolsos,
com assinatura **V2-HMAC-SHA256** (`login+date+body`) e **credenciais por produto/MID**
(checkout/card/remittance/payout — a dLocal emite um trio por produto; toda operação declara o
produto).

Validação viva (2026-07-15): probes de leitura POR PRODUTO executados nos 4 MIDs
(payments-methods 200 com dados reais) + 404 de domínio code 4000 (payments e refunds) + cotação
real de câmbio (`GET /currency-exchanges?from=USD&to=BRL`). 9 unit + 7 sandbox verdes.
`CreatePaymentAsync`/`CreateRefundAsync` = **financial-write**, nunca executados (goal §0.5).
Cashout/payout (API legada separada) fora do v1 por decisão registrada.

Secrets: `pass cambio-real-v2/dlocal/demo-env`. Discovery: `docs/providers/dlocal/discovery.md`.
