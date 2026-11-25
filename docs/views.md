# Кастомные разделы (views/blocks) — контракт v1

Приложение — не только доска. ИИ сам создаёт разделы, а клиенты (ПК, телефон, веб) их рисуют.
Ничего не ломается при обновлении: блоки лежат отдельным ключом `blocks` в файле доски,
неизвестный `kind` клиент просто пропускает, `tasks`/`sections` остаются как были.

## Модель блока

```json
{
  "id": "v1a2b3c4",
  "kind": "metric",
  "title": "Обучение ada-v6",
  "order": 10,
  "size": "sm",
  "body": { "value": "6875/6875", "unit": "шаг", "sub": "loss 0.2648", "history": [{ "t": 1764000000000, "v": 99 }] },
  "source": { "path": "D:/finetune/runs/logs/marathon.log", "lines": 40 },
  "updated": "2026-11-25T14:00:00.000Z"
}
```

- `id` — `[a-zA-Z0-9_-]`, до 32 символов, генерируется сервером если не задан.
- `kind` — `metric`, `progress`, `status`, `text`, `log`, `links`, `chart`, `code`, `tasks`, `activity`.
  Незнакомый kind клиент не рисует и не удаляет.
- `order` — целое, меньше = выше. `size` — `sm` (1 колонка), `md`, `wide` (во всю ширину).
- `body` — свободный объект, ограничение 64 КБ на блок.
- `source.path` — для `kind: "log"`: сервер сам подтягивает хвост файла с ПК (любой путь, до 128 КБ),
  клиенту приходит в поле `tail`. Так телефон следит за чем угодно: логи, обучение, сборка.

Всего блоков на доску — до 40.

### Что кладём в body

| kind       | body                                                                    |
|------------|-------------------------------------------------------------------------|
| `metric`   | `{value, unit, sub, history:[{t,v}]}` — число + спарклайн                 |
| `progress` | `{value:0..100, label, eta}`                                             |
| `status`   | `{items:[{label, state:"ok\|warn\|err\|idle", note}]}` — светофоры        |
| `text`     | `{text}` — простой текст с переводами строк                              |
| `log`      | `{path, lines}` (плюс серверный `tail`)                                  |
| `links`    | `{items:[{label, url, note}]}`                                           |
| `chart`    | `{unit, series:[{label, points:[v,...]}]}`                               |
| `code`     | `{lang, filename, content}` — версия и sha считает сервер                |
| `tasks`    | `{}` — обычный список задач (клиент рисует из `/api/b/tasks`)             |
| `activity` | `{}` — лента действий                                                    |

## API (все запросы — с `?b=<board>` и ключом/куки `nxt`)

- `GET  /api/b/views?b=` → `{ok, version, blocks:[...]}`; у `log`-блоков добавлен `tail`.
- `POST /api/b/view/set` `{id?, kind, title, body, source, order, size}` → `{ok, block}` (создать/обновить).
- `POST /api/b/view/del` `{id}` → `{ok, blocks}`.
- `POST /api/b/sync` `{tasks, sections, mode}` → полная замена задач/секций (ПК ведёт доску, блоки не трогаются).
- `GET  /api/b/code?b=&id=` → `{ok, code:{id, title, lang, filename, content, version, sha}}`.
- `POST /api/b/code/save` `{id, content, ifVersion}` → `{ok, version, sha}`;
  если `ifVersion` не совпал с текущей — `409 {ok:false, error:"conflict", version, sha, content}`,
  правки не теряются: клиент показывает конфликт и даёт забрать актуальную версию.

`code` — обычный текстовый документ: телефон тянет изменения с ПК (`GET`), может отправить свою правку
(`save` с `ifVersion`), история версий — 5 последних, sha256/16.

## CLI (пишет ИИ, чтобы создавать разделы самому)

```
NexusBoard.exe --view-add "Обучение" --kind progress --body "{\"value\":82,\"label\":\"ada-v6\"}"
NexusBoard.exe --view-set <id> --body "{\"value\":91}"
NexusBoard.exe --view-del <id>
NexusBoard.exe --view-list [--json]
NexusBoard.exe --code-push <id> --title "Патч сервера" --file E:\patch.diff
NexusBoard.exe --code-pull <id> --out E:\patch.diff
```

`--code-push` без `--file` берёт текст из stdin. `--body` можно писать как `@file.json`.
