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
| — | cashout (`api_curl/cashout_api`, legado antigo) | **fora do escopo v1** (decisão) | financial-write | ⚪ payout DLocal segue como gap conhecido do PROVIDER-MAP |
| — | webhooks (notification_url) | fora do gateway (decisão; legado consome direto) | — | ⚪ |

## 5. Decisões e lacunas

1. Cashout/payout (API legada `api_curl/cashout_api`, formato distinto) fora do v1 — o
   PROVIDER-MAP já registra payout DLocal como gap; incremento futuro com discovery próprio.
2. Refunds/chargebacks fora do v1 (sem uso confirmado no fluxo avaliado).
3. `additional_risk_data` exposto cru (o legado envia um bloco grande de risco no PIX — política
   de consumidor).
4. 6 MIDs mencionados no PROVIDER-MAP vs 4 no pass — os 4 disponíveis foram validados; MIDs
   adicionais (se existirem) entram pelo mesmo dicionário `Products` sem mudança de código.

## 6. Nenhuma contradição arquitetural

Padrão canônico Sync + SDK/gateway standalone; credencial-por-produto modelada explicitamente.
