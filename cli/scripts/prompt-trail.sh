#!/bin/bash

# find the prompt-trail binary across common install locations
BINARY=""
for path in \
  "$HOME/.npm-global/bin/prompt-trail" \
  "/usr/local/bin/prompt-trail" \
  "/opt/homebrew/bin/prompt-trail" \
  "$(which prompt-trail 2>/dev/null)"
do
  if [ -x "$path" ]; then
    BINARY="$path"
    break
  fi
done

if [ -z "$BINARY" ]; then
  echo "prompt-trail: binary not found" >&2
  exit 0
fi

# pipe stdin into the binary
cat - | "$BINARY" record

exit 0