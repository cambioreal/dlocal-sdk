# dLocal — Discovery

Status: descoberta e SDK concluídos (2026-07-15). **Pré-condição do goal satisfeita**: os probes
de leitura não financeiros POR PRODUTO foram definidos e executados ao vivo ANTES do código —
`GET /payments-methods?country=BR` em cada MID + `GET /payments/{fictício}` (404 autenticado).
Provider order position: **9 of 9**.
Verified: 2026-07-15, contra `pass cambio-real-v2/dlocal/demo-env` no sandbox vivo
(`sandbox.dlocal.com`) + legado `cerebro` (read-only).

## 1. Perfil e credenciais por produto

**`Sync`** payin multi-método (PIX `PQ`, boleto, cartão, TED) via Payins API v2.1. A dLocal emite
**um MID (login/key/secret) por produto** — 4 no pass: `checkout`, `card`, `remittance`,
`payout`. Toda operação do SDK declara o produto; a assinatura usa a secret do MID.

## 2. Auth/assinatura e convenções

- Headers: `X-Date` (ISO .fffZ), `X-Login`, `X-Trans-Key`, `X-Version: 2.1`,
  `Authorization: V2-HMAC-SHA256, Signature: hmac_sha256(login+date+body, secret)` — confirmado
  no legado (`AbstractRequest::signature`) e ao vivo nos 4 MIDs.
- snake_case; statuses UPPER (`PENDING`/`PAID`/`REJECTED`/…, conjunto aberto ⇒ strings).
- Erro: `{code: número, message}` — confirmado ao vivo (404 = code 4000 "Payment not found").

## 3. Probes por produto (definidos e executados — evidência viva 2026-07-15)

| MID | Probe | Resultado |
|---|---|---|
| checkout | GET /payments-methods?country=BR | ✅ 200, 18 métodos (PIX QR etc.) |
| card | idem | ✅ 200, 10 métodos (Visa Debit etc.) |
| remittance | idem | ✅ 200, 1 método (PIX) |
| payout | idem | ✅ 200, 18 métodos (bank transfers) |
| checkout | GET /payments/{fictício} | ✅ 404 code 4000 — acesso real |

## 4. Matriz de cobertura

| # | Endpoint | Recurso SDK | Efeito | Status sandbox |
|---|---|---|---|---|
| 1 | `GET /payments-methods` | `GetPaymentMethodsAsync(product)` | read (probe canônico por MID) | ✅ vivo (4/4 MIDs) |
| 2 | `GET /payments/{id}` | `GetPaymentAsync(product, id)` | read (fonte de verdade de status) | ✅ vivo (404 code 4000) |
| 3 | `POST /payments` | `CreatePaymentAsync(product, req)` | **financial-write** (payin PIX/boleto/cartão/TED por payment_method_id) | 🔴 contrato-only (§0.5) |
| 4 | `GET /currency-exchanges` | `GetCurrencyExchangeAsync(product, from, to)` | read (cotação; dLocal só aceita `from=USD` no momento) | ✅ vivo (2026-07-15, checkout) |
| 5 | `POST /refunds` | `CreateRefundAsync(product, req)` | **financial-write** (reembolso de payin) | 🔴 contrato-only (§0.5), nunca exercitado |
| 6 | `GET /refunds/{id}` | `GetRefundAsync(product, id)` | read (consulta reembolso) | ✅ vivo (404 de domínio, id fictício — sem reembolso real criado) |
| — | cashout (`api_curl/cashout_api`, legado antigo) | **fora do escopo v1** (decisão) | financial-write | ⚪ payout DLocal segue como gap conhecido do PROVIDER-MAP |
| — | webhooks (notification_url) | fora do gateway (decisão; legado consome direto) | — | ⚪ |
| — | chargebacks (`GET /chargebacks/{id}`) | não implementado | read | ⚪ gap remanescente (fora deste incremento) |

## 5. Decisões e lacunas

1. Cashout/payout (API legada `api_curl/cashout_api`, formato distinto) fora do v1 — o
   PROVIDER-MAP já registra payout DLocal como gap; incremento futuro com discovery próprio.
2. ~~Refunds/chargebacks fora do v1~~ — **refunds implementado em 2026-07-15** (`GET
   /currency-exchanges`, `POST /refunds`, `GET /refunds/{id}`, gaps P0 do
   `provider-protocol/docs/gateways/coverage/dlocal.md`). Chargebacks (`GET /chargebacks/{id}`)
   segue fora do v1 — sem uso confirmado, não endereçado neste incremento.
3. `additional_risk_data` exposto cru (o legado envia um bloco grande de risco no PIX — política
   de consumidor).
4. 6 MIDs mencionados no PROVIDER-MAP vs 4 no pass — os 4 disponíveis foram validados; MIDs
   adicionais (se existirem) entram pelo mesmo dicionário `Products` sem mudança de código.
5. `GET /currency-exchanges` **não tem parâmetro `amount`** — confirmado em duas páginas da doc
   oficial (`docs.dlocal.com/reference/get-an-exchange-rate` e
   `docs.dlocal.com/api-documentation/payins-api-reference/currency-exchange`), ambas só listam
   `from`/`to`. `GetCurrencyExchangeAsync` reflete isso (sem parâmetro `amount`).
6. `DlocalRefund.CreatedDate` é `string?`, não `DateTimeOffset?` (diferente de
   `DlocalPayment.CreatedDate`): o exemplo oficial de resposta usa offset sem dois-pontos
   (`2018-09-06T22:03:03.000+0000`), formato que o conversor padrão do `System.Text.Json` rejeita
   (confirmado empiricamente com um teste isolado). O mesmo risco existe, não corrigido, em
   `DlocalPayment.CreatedDate` — nunca exercitado ao vivo com um payment real (§0.5), então nunca
   detectado.

## 6. Nenhuma contradição arquitetural

Padrão canônico Sync + SDK/gateway standalone; credencial-por-produto modelada explicitamente.
