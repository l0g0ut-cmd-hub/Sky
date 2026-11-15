#!/bin/sh
# Удалённые команды в игру (RemoteCommands): tools/sky-cmd.sh "near" "wait 3" "shot враг рядом"
# Пишет Sky-reports/config/commands.json (id +1, срок 10 мин) и пушит. Игра подхватит за ~15 с, итог — reports/ack.txt.
set -e
R=${SKY_REPORTS:-/home/claude/sky-reports}
cd "$R"; git pull -q --rebase
F=config/commands.json
ID=$(python3 -c "import json,sys;print(json.load(open('$F')).get('id',0)+1)" 2>/dev/null || echo 1)
python3 - "$ID" "$@" > "$F" <<'PY'
import json, sys, time
print(json.dumps({"id": int(sys.argv[1]), "expires": int(time.time()) + 600, "cmds": sys.argv[2:]}, ensure_ascii=False))
PY
git add "$F"; git commit -qm "команды #$ID: $*"
for i in 1 2 3 4 5; do git push -q 2>/dev/null && break; git pull -q --rebase; done   # игра пушит отчёты постоянно — гонка
echo "команды #$ID отправлены: $*"
