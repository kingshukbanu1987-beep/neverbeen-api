#!/usr/bin/env python3
"""Verifies the Messenger page (wide layout) change to neverbeen-api
(see neverbeen-api-community-messenger-wide.patch).

On tablets, laptops and wide screens the website's Messenger page lists every previous and
current chat (Facebook Messenger style), newest first, with the last message, who wrote it
and an unread badge. The API's conversation list was ordered by creation time, and a chat
(re)opened with POST /api/messages/conversations came back without its last message or
unread count. This patch:

  * adds ConversationDto.LastActivityUtc (newest message, else creation time);
  * builds every conversation DTO in one place (BuildConversationDtos) with LastMessage,
    UnreadCount and LastActivityUtc, for GET and for POST;
  * orders GET /api/messages/conversations by LastActivityUtc (newest first);
  * counts only the other members' messages as unread.

No database change: every value comes from existing columns.

There is no .NET SDK in the sandbox, so this script is the automated evidence:

  A  the patch applies cleanly to neverbeen-api (main b0f216e) and touches only
     Controllers/MessagesController.cs and Dtos/MessengerDtos.cs
  B  the touched .cs files are structurally sound (braces / brackets / parens balanced;
     tree-sitter syntax check when the packages are installed)
  C  ConversationDto has LastActivityUtc
  D  BuildConversationDtos fills LastMessage, UnreadCount and LastActivityUtc, and unread
     excludes the member's own messages
  E  GetConversations uses BuildConversationDtos and orders by LastActivityUtc desc
  F  the website reads lastActivityUtc (src/app/services/community.service.ts)

Run:  python3 neverbeen-api-community-messenger-wide-verification.py [path-to-neverbeen-api]
"""

import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-community-messenger-wide.patch')
WEB_SERVICE = os.path.join(HERE, '..', '..', 'src', 'app', 'services', 'community.service.ts')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
API_COMMIT = 'b0f216e'
EXPECTED_FILES = {'Controllers/MessagesController.cs', 'Dtos/MessengerDtos.cs'}

failures = []


def check(label, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + label + ('' if ok or not detail else f'  -- {detail}'))
    if not ok:
        failures.append(label)


def git(args, cwd):
    return subprocess.run(['git'] + args, cwd=cwd, capture_output=True, text=True)


def balanced(src):
    # Strip strings, chars and comments before counting.
    cleaned = re.sub(r'//[^\n]*', '', src)
    cleaned = re.sub(r'/\*.*?\*/', '', cleaned, flags=re.S)
    cleaned = re.sub(r'@"(?:[^"]|"")*"', '""', cleaned)
    cleaned = re.sub(r'"(?:\\.|[^"\\])*"', '""', cleaned)
    cleaned = re.sub(r"'(?:\\.|[^'\\])'", "''", cleaned)
    pairs = {')': '(', ']': '[', '}': '{'}
    stack = []
    for ch in cleaned:
        if ch in '([{':
            stack.append(ch)
        elif ch in pairs:
            if not stack or stack.pop() != pairs[ch]:
                return False
    return not stack


def method_body(src, signature):
    start = src.find(signature)
    if start < 0:
        return ''
    i = src.find('{', start)
    depth = 0
    for j in range(i, len(src)):
        if src[j] == '{':
            depth += 1
        elif src[j] == '}':
            depth -= 1
            if depth == 0:
                return src[i:j + 1]
    return ''


with tempfile.TemporaryDirectory() as tmp:
    work = os.path.join(tmp, 'api')
    clone = git(['clone', '-q', API_DIR, work], cwd=tmp)
    check('A0 neverbeen-api checkout available', clone.returncode == 0, clone.stderr.strip())
    if clone.returncode != 0:
        sys.exit(1)
    git(['checkout', '-q', API_COMMIT], cwd=work)

    applied = git(['apply', '--check', PATCH], cwd=work)
    check(f'A1 patch applies cleanly to {API_COMMIT}', applied.returncode == 0, applied.stderr.strip())
    git(['apply', PATCH], cwd=work)
    touched = set(git(['diff', '--name-only'], cwd=work).stdout.split())
    check('A2 patch touches only the Messenger controller and DTOs', touched == EXPECTED_FILES, str(touched))

    sources = {}
    for rel in EXPECTED_FILES:
        with open(os.path.join(work, rel), encoding='utf-8') as fh:
            sources[rel] = fh.read()
        check(f'B1 {rel} is structurally balanced', balanced(sources[rel]))

    try:
        import tree_sitter_c_sharp
        from tree_sitter import Language, Parser

        parser = Parser(Language(tree_sitter_c_sharp.language()))
        for rel, src in sources.items():
            tree = parser.parse(src.encode('utf-8'))
            check(f'B2 {rel} parses without syntax errors (tree-sitter)', not tree.root_node.has_error)
    except Exception:  # noqa: BLE001 - optional dependency
        print('SKIP B2 tree-sitter not installed (pip install tree-sitter tree-sitter-c-sharp)')

    dto = sources['Dtos/MessengerDtos.cs']
    conv_dto = method_body(dto, 'public class ConversationDto')
    check('C1 ConversationDto.LastActivityUtc exists', 'public DateTime LastActivityUtc { get; set; }' in conv_dto)

    ctl = sources['Controllers/MessagesController.cs']
    build = method_body(ctl, 'private async Task<List<ConversationDto>> BuildConversationDtos(')
    check('D1 BuildConversationDtos fills LastMessage', re.search(r'LastMessage\s*=\s*last == null', build) is not None)
    check('D2 BuildConversationDtos fills UnreadCount', 'UnreadCount = unread' in build)
    check('D3 BuildConversationDtos fills LastActivityUtc from the newest message or creation',
          re.search(r'LastActivityUtc\s*=\s*last != null && last\.SentAtUtc > conversation\.CreatedAtUtc', build) is not None)
    check('D4 unread excludes the member\'s own messages', 'm.SenderId != myId' in build)
    check('D5 newest message is picked by SentAtUtc then Id',
          re.search(r'OrderByDescending\(m => m\.SentAtUtc\)\s*\.ThenByDescending\(m => m\.Id\)', build) is not None)

    get = method_body(ctl, 'public async Task<ActionResult<List<ConversationDto>>> GetConversations(')
    check('E1 GetConversations builds its rows with BuildConversationDtos', 'BuildConversationDtos(conversations, myId' in get)
    check('E2 GetConversations orders by LastActivityUtc (newest first)', 'OrderByDescending(c => c.LastActivityUtc)' in get)
    check('E3 GetConversations no longer orders by CreatedAtUtc', 'OrderByDescending(c => c.CreatedAtUtc)' not in get)
    check('E4 POST conversations still returns BuildConversationDtos rows', ctl.count('BuildConversationDtos(new List<Conversation>') == 3)

with open(WEB_SERVICE, encoding='utf-8') as fh:
    web = fh.read()
check('F1 website reads lastActivityUtc for the chats panel order',
      'c.lastActivityUtc ?? c.lastMessage?.sentAtUtc ?? c.createdAtUtc' in web)

print()
print('ALL CHECKS PASSED' if not failures else f'{len(failures)} CHECK(S) FAILED')
sys.exit(1 if failures else 0)
