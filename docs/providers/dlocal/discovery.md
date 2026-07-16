# dLocal — Discovery

Status: descoberta e SDK concluídos (2026-07-15; gaps P2 — cancelamento e chargebacks —
adicionados em 2026-07-16, SDK 0.3.0). **Pré-condição do goal satisfeita**: os probes de leitura
não financeiros POR PRODUTO foram definidos e executados ao vivo ANTES do código —
`GET /payments-methods?country=BR` em cada MID + `GET /payments/{fictício}` (404 autenticado).
Provider order position: **9 of 9**.
Verified: 2026-07-15, contra `pass cambio-real-v2/dlocal/demo-env` no sandbox vivo
(`sandbox.dlocal.com`) + legado `cerebro` (read-only). Cancelamento/chargebacks (2026-07-16)
validados só por contrato/mock — sem acesso a `pass` neste turno (ver §5.9).

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
| 7 | `POST /payments/{id}/cancel` | `CancelPaymentAsync(product, id)` | **write** (cancela autorização de cartão não capturada OU payment pendente de APM — mesmo path oficial para os dois cenários) | 🔴 contrato-only (§0.5), nunca exercitado |
| 8 | `GET /chargebacks/{id}` | `GetChargebackAsync(product, id)` | read (consulta chargeback; dLocal não documenta endpoint de listagem) | ⚪ contrato-only nesta sessão (mock verde; sandbox opt-in escrito mas não executado — sem acesso a `pass` neste turno) |
| — | cashout (`api_curl/cashout_api`, legado antigo) | **fora do escopo v1** (decisão) | financial-write | ⚪ payout DLocal segue como gap conhecido do PROVIDER-MAP |
| — | webhooks (notification_url) | fora do gateway (decisão; legado consome direto) | — | ⚪ |
| — | `POST /secure_payments` (PAN/CVV cru) | não implementado | financial-write | ⚪ deliberadamente fora — decisão de PCI/produto |
| — | `POST /payments/wallet/{token}/cancel` | não implementado | write | ⚪ deliberadamente fora — nicho |

## 5. Decisões e lacunas

1. Cashout/payout (API legada `api_curl/cashout_api`, formato distinto) fora do v1 — o
   PROVIDER-MAP já registra payout DLocal como gap; incremento futuro com discovery próprio.
2. ~~Refunds/chargebacks fora do v1~~ — **refunds implementado em 2026-07-15** (`GET
   /currency-exchanges`, `POST /refunds`, `GET /refunds/{id}`, gaps P0 do
   `provider-protocol/docs/gateways/coverage/dlocal.md`). **Cancelamento e chargebacks
   implementados em 2026-07-16** (`POST /payments/{id}/cancel`, `GET /chargebacks/{id}`, gaps P2
   do mesmo coverage doc). `secure_payments` (PAN/CVV cru) e cancelamento de token de wallet
   seguem deliberadamente fora — não endereçados neste incremento (PCI/produto e nicho,
   respectivamente).
7. **Inconsistência de tipo em `status_code` entre recursos dLocal** — descoberta ao implementar
   cancelamento/chargebacks (2026-07-16): os exemplos oficiais de `cancel-an-authorization`,
   `cancel-alternative-payment` e `retrieve-a-chargeback` devolvem `"status_code"` como **string**
   (`"400"`/`"200"`), enquanto o exemplo de `retrieve-a-refund` devolve **número** (`200`). O SDK
   agora usa `JsonNumberHandling.AllowReadingFromString` globalmente (`DlocalClient.CreateJson`)
   para tolerar as duas formas em todo `int? StatusCode` (`DlocalPayment`, `DlocalRefund`,
   `DlocalChargeback`) — sem essa mudança, um cancelamento ou chargeback real quebraria a
   desserialização. Não afeta campos numéricos que já eram consistentemente números
   (`amount`, `rate`).
8. **Chargebacks não têm endpoint de listagem público** — confirmado via `docs.dlocal.com/llms.txt`
   (só `retrieve-a-chargeback`, `retrieve-a-chargeback-status`, `chargeback-asynchronous-notification`,
   `simulate-chargeback-sandbox-only` existem); só a consulta individual por id foi implementada.
9. **Teste sandbox de chargeback (`ChargebackGetWithFictitiousIdReturnsDomain404`) escrito mas NÃO
   executado nesta sessão** — sem acesso a `pass cambio-real-v2/dlocal/demo-env` neste turno (regra
   "nunca `pass show` um segredo"). Mock/contrato (13 unit + 13 contrato de gateway, todos verdes)
   cobrem o resto. Fica opt-in via `[Trait("Category","Sandbox")]` para quando alguém rodar com as
   env vars.
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
