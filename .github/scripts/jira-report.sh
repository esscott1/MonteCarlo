#!/usr/bin/env bash
# Reports an agent run back to its Jira story:
#   jira-report.sh comment "<text>"            adds a comment
#   jira-report.sh transition "<status name>"  moves the story (transition looked up by its name or its target status)
# Needs JIRA_BASE_URL, JIRA_EMAIL, JIRA_API_TOKEN and ISSUE_KEY in the environment. Never fails the run: the pull
# request is what matters, so a Jira problem is a warning in the log.
set -uo pipefail

warn() { echo "::warning::$1"; exit 0; }

[[ -n "${JIRA_EMAIL:-}" && -n "${JIRA_API_TOKEN:-}" ]] || warn "JIRA_EMAIL/JIRA_API_TOKEN aren't set, so the story wasn't updated."
[[ "${ISSUE_KEY:-}" =~ ^SCRUM-[0-9]+$ ]] || warn "No valid issue key, so the story wasn't updated."

api="$JIRA_BASE_URL/rest/api/2/issue/$ISSUE_KEY"
auth=(-u "$JIRA_EMAIL:$JIRA_API_TOKEN" -H "Content-Type: application/json" -sS -f)

case "${1:-}" in
  comment)
    jq -n --arg text "$2" '{body: $text}' | curl "${auth[@]}" -X POST "$api/comment" --data @- > /dev/null \
      || warn "Couldn't comment on $ISSUE_KEY."
    echo "Commented on $ISSUE_KEY."
    ;;
  transition)
    id="$(curl "${auth[@]}" "$api/transitions" \
      | jq -r --arg name "$2" '.transitions[]
          | select((.name | ascii_downcase) == ($name | ascii_downcase) or (.to.name | ascii_downcase) == ($name | ascii_downcase))
          | .id' | head -n 1)" || warn "Couldn't read $ISSUE_KEY's transitions."
    [[ -n "$id" ]] || warn "$ISSUE_KEY has no transition to \"$2\" from its current status."
    jq -n --arg id "$id" '{transition: {id: $id}}' | curl "${auth[@]}" -X POST "$api/transitions" --data @- > /dev/null \
      || warn "Couldn't move $ISSUE_KEY to \"$2\"."
    echo "Moved $ISSUE_KEY to $2."
    ;;
  *)
    warn "Unknown jira-report.sh command: ${1:-}"
    ;;
esac
