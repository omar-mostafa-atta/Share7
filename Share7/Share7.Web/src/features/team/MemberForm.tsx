import { useMemo, useState } from 'react'
import { Check, ChevronRight, Copy, Eye, EyeOff, WandSparkles } from 'lucide-react'
import { Button, Subhead } from '../../components/ui/primitives'
import { Def, DefList, Note, SearchBox, Segmented } from '../../components/ui/bits'
import { Field, Input, Switch } from '../../components/ui/form'
import { generatePassword } from '../users/password'
import {
  STUDIO_ROLES,
  useScopeOptions,
  type MemberDraft,
  type PasswordRules,
  type ScopeTreeNode,
  type StaffScope,
  type StudioRole,
} from './data'

// ===========================================================================
// A content-team member: who they are, what they do, what they work on
//
// One form for the three places a member is described — added from Users by
// an Admin, added from Team & Access by a SuperAdmin, and a pre-Studio account
// given its profile — so the three can never ask for different things.
//
// It is a single form in three labelled parts rather than a wizard: the parts
// depend on each other (a Lead over "everything" is a different decision from
// a Lead over one subject), and seeing all three at once is how the person
// adding them notices the combination before the link goes out.
// ===========================================================================

const USERNAME_PATTERN = /^[A-Za-z0-9._@+-]+$/

export function emptyDraft(): MemberDraft {
  return {
    fullName: '',
    username: '',
    password: '',
    workEmail: '',
    jobTitle: '',
    interfaceLanguage: 'en',
    studioRole: 'Author',
    allNodes: true,
    nodeIds: [],
    allLanguages: true,
    languageIds: [],
  }
}

/** A member's existing access as a draft, for changing it. */
export function draftFromScope(role: StudioRole, scope: StaffScope): MemberDraft {
  return {
    ...emptyDraft(),
    studioRole: role,
    allNodes: scope.allNodes,
    nodeIds: scope.nodes.filter((n) => n.exists).map((n) => n.id),
    allLanguages: scope.allLanguages,
    languageIds: scope.languages.map((l) => l.id),
  }
}

/**
 * What a staff password still lacks, as phrases that finish "Needs …", checked against the rules the
 * server holds it to (Share7.Infrastructure/Staff/StaffPasswordPolicy.cs). The server still decides.
 */
export function staffPasswordProblems(password: string, username: string, rules: PasswordRules): string[] {
  const problems: string[] = []
  if (password.length < rules.minimumLength) problems.push(`at least ${rules.minimumLength} characters`)
  if (rules.requireLowercase && !/[a-z]/.test(password)) problems.push('a lower-case letter')
  if (rules.requireUppercase && !/[A-Z]/.test(password)) problems.push('an upper-case letter')
  if (rules.requireDigit && !/[0-9]/.test(password)) problems.push('a digit')
  const name = username.trim().toLowerCase()
  if (name.length >= 3 && password.toLowerCase().includes(name)) problems.push('not to contain the username')
  return problems
}

/** What stops the draft being sent, field by field. Nothing typed yet is not a problem. */
export function draftProblems(
  draft: MemberDraft,
  parts: { who: boolean; username: boolean; access: boolean; password?: PasswordRules },
) {
  const problems: Partial<Record<'fullName' | 'username' | 'password' | 'workEmail' | 'nodes' | 'languages', string>> = {}
  const username = draft.username.trim()

  if (parts.who && !draft.fullName.trim()) problems.fullName = 'Their name, as the team knows them.'
  if (parts.username) {
    if (username.length < 3) problems.username = 'At least 3 characters.'
    else if (!USERNAME_PATTERN.test(username)) problems.username = 'Letters, digits and - . _ @ + only — no spaces.'
  }
  if (parts.password) {
    const missing = staffPasswordProblems(draft.password, username, parts.password)
    if (missing.length) problems.password = `Needs ${missing.join(', ')}.`
  }
  if (parts.who && draft.workEmail.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(draft.workEmail.trim()))
    problems.workEmail = 'That does not look like an email address.'
  if (parts.access && !draft.allNodes && draft.nodeIds.length === 0) problems.nodes = 'Choose at least one part of the curriculum.'
  if (parts.access && !draft.allLanguages && draft.languageIds.length === 0) problems.languages = 'Choose at least one language.'

  return problems
}

// ---------------------------------------------------------------------------
// The parts
// ---------------------------------------------------------------------------

export function WhoFields({
  draft,
  onChange,
  withUsername,
  touched,
  rules,
}: {
  draft: MemberDraft
  onChange: (next: MemberDraft) => void

  /** A new member: their username and the password the admin sets for them. */
  withUsername: boolean
  touched: boolean
  rules?: PasswordRules
}) {
  const problems = draftProblems(draft, { who: true, username: withUsername, access: false, password: withUsername ? rules : undefined })
  const show = (key: keyof typeof problems, typed: string) => (touched || typed ? problems[key] : null)

  return (
    <div className="s7-stack">
      <Subhead>Who they are</Subhead>
      <div className="s7-form-pair">
        <Field label="Full name" error={show('fullName', '')}>
          <Input value={draft.fullName} onChange={(e) => onChange({ ...draft, fullName: e.target.value })} autoComplete="off" autoFocus />
        </Field>
        {withUsername ? (
          <Field label="Username" error={show('username', draft.username)} hint="What they type to sign in to the Studio.">
            <Input
              value={draft.username}
              onChange={(e) => onChange({ ...draft, username: e.target.value })}
              invalid={!!show('username', draft.username)}
              autoComplete="off"
              spellCheck={false}
              placeholder="e.g. layla.content"
            />
          </Field>
        ) : null}
      </div>
      {withUsername && rules ? (
        <PasswordField
          value={draft.password}
          onChange={(password) => onChange({ ...draft, password })}
          rules={rules}
          username={draft.username}
          error={touched || draft.password ? (problems.password ?? null) : null}
        />
      ) : null}
      <div className="s7-form-pair">
        <Field label="Job title" hint="Optional.">
          <Input value={draft.jobTitle} onChange={(e) => onChange({ ...draft, jobTitle: e.target.value })} placeholder="e.g. Mathematics specialist" />
        </Field>
        <Field label="Work email" error={show('workEmail', draft.workEmail)} hint="Optional. Nothing is sent to it.">
          <Input type="email" value={draft.workEmail} onChange={(e) => onChange({ ...draft, workEmail: e.target.value })} />
        </Field>
      </div>
      <Field label="The Studio opens in" hint="They can switch it themselves at any time.">
        <div>
          <Segmented
            layoutId={`member-language-${withUsername ? 'new' : 'existing'}`}
            value={draft.interfaceLanguage}
            onChange={(value) => onChange({ ...draft, interfaceLanguage: value })}
            options={[
              { value: 'en', label: 'English' },
              { value: 'ar', label: 'العربية' },
            ]}
          />
        </div>
      </Field>
    </div>
  )
}

export function AccessFields({
  draft,
  onChange,
  touched,
  idPrefix,
}: {
  draft: MemberDraft
  onChange: (next: MemberDraft) => void
  touched: boolean
  idPrefix: string
}) {
  const options = useScopeOptions(true)
  const problems = draftProblems(draft, { who: false, username: false, access: true })
  const role = STUDIO_ROLES.find((r) => r.value === draft.studioRole)!

  return (
    <div className="s7-stack">
      <Subhead>What they do</Subhead>
      <Field label="Studio role" hint={role.description}>
        <div>
          <Segmented
            layoutId={`${idPrefix}-studio-role`}
            value={draft.studioRole}
            onChange={(value) => onChange({ ...draft, studioRole: value })}
            options={STUDIO_ROLES.map((r) => ({ value: r.value, label: r.label }))}
          />
        </div>
      </Field>

      <Subhead>What they work on</Subhead>
      <Field label="Parts of the curriculum" error={touched || draft.nodeIds.length ? problems.nodes : null}>
        <div className="s7-stack" style={{ gap: '0.6rem' }}>
          <Switch
            checked={draft.allNodes}
            onChange={(allNodes) => onChange({ ...draft, allNodes })}
            label="All of it, including anything added later"
          />
          {draft.allNodes ? null : (
            <ScopeTree
              nodes={options.data.nodes}
              loading={options.loading}
              chosen={draft.nodeIds}
              onChange={(nodeIds) => onChange({ ...draft, nodeIds })}
            />
          )}
        </div>
      </Field>

      <Field label="Languages" error={touched ? problems.languages : null}>
        <div className="s7-stack" style={{ gap: '0.6rem' }}>
          <Switch
            checked={draft.allLanguages}
            onChange={(allLanguages) => onChange({ ...draft, allLanguages })}
            label="Every language"
          />
          {draft.allLanguages ? null : (
            <div className="s7-inline-badges" role="group" aria-label="Languages">
              {options.data.languages.map((language) => {
                const on = draft.languageIds.includes(language.id)
                return (
                  <button
                    key={language.id}
                    type="button"
                    className={`s7-chip ${on ? 's7-chip-on' : ''}`}
                    aria-pressed={on}
                    onClick={() =>
                      onChange({
                        ...draft,
                        languageIds: on ? draft.languageIds.filter((id) => id !== language.id) : [...draft.languageIds, language.id],
                      })
                    }
                  >
                    {language.name}
                  </button>
                )
              })}
            </div>
          )}
        </div>
      </Field>
    </div>
  )
}

// ---------------------------------------------------------------------------
// The curriculum, to choose from
// ---------------------------------------------------------------------------

/**
 * The curriculum down to chapters. Choosing a part covers everything under it, so its branch shows
 * as included and cannot be chosen again — the server would refuse a part and its own parent.
 */
function ScopeTree({
  nodes,
  loading,
  chosen,
  onChange,
}: {
  nodes: ScopeTreeNode[]
  loading: boolean
  chosen: string[]
  onChange: (ids: string[]) => void
}) {
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [search, setSearch] = useState('')

  const { children, parentOf, byId } = useMemo(() => {
    const children = new Map<string | null, ScopeTreeNode[]>()
    const parentOf = new Map<string, string | null>()
    const byId = new Map<string, ScopeTreeNode>()
    for (const node of nodes) {
      byId.set(node.id, node)
      parentOf.set(node.id, node.parentId)
      const list = children.get(node.parentId) ?? []
      list.push(node)
      children.set(node.parentId, list)
    }
    for (const list of children.values()) list.sort((a, b) => a.order - b.order)
    return { children, parentOf, byId }
  }, [nodes])

  const chosenSet = useMemo(() => new Set(chosen), [chosen])

  const ancestors = (id: string) => {
    const out: string[] = []
    let at = parentOf.get(id) ?? null
    while (at) {
      out.push(at)
      at = parentOf.get(at) ?? null
    }
    return out
  }

  const coveredByParent = (id: string) => ancestors(id).some((a) => chosenSet.has(a))

  const descendants = (id: string): string[] =>
    (children.get(id) ?? []).flatMap((child) => [child.id, ...descendants(child.id)])

  const toggle = (id: string) => {
    if (chosenSet.has(id)) {
      onChange(chosen.filter((c) => c !== id))
      return
    }
    // Choosing a part takes over anything already chosen beneath it.
    const beneath = new Set(descendants(id))
    onChange([...chosen.filter((c) => !beneath.has(c)), id])
  }

  const trail = (node: ScopeTreeNode) =>
    [...ancestors(node.id).reverse().map((id) => byId.get(id)!.title.en), node.title.en].join(' › ')

  if (loading) return <span className="s7-hint">Loading the curriculum…</span>
  if (!nodes.length) return <span className="s7-hint">The curriculum could not be loaded.</span>

  const query = search.trim().toLowerCase()
  const matches = query
    ? nodes.filter((n) => n.title.en.toLowerCase().includes(query) || (n.title.ar ?? '').includes(search.trim())).slice(0, 60)
    : []

  const row = (node: ScopeTreeNode, depth: number, withTrail: boolean) => {
    const covered = coveredByParent(node.id)
    const on = chosenSet.has(node.id) || covered
    const kids = children.get(node.id) ?? []
    const expanded = open.has(node.id)

    return (
      <li key={node.id}>
        <div className="s7-scope-row" style={{ paddingInlineStart: `${0.35 + depth * 1.1}rem` }}>
          {kids.length && !withTrail ? (
            <button
              type="button"
              className={`s7-scope-twist ${expanded ? 'is-open' : ''}`}
              aria-label={expanded ? `Close ${node.title.en}` : `Open ${node.title.en}`}
              aria-expanded={expanded}
              onClick={() => {
                const next = new Set(open)
                if (expanded) next.delete(node.id)
                else next.add(node.id)
                setOpen(next)
              }}
            >
              <ChevronRight size={14} />
            </button>
          ) : (
            <span className="s7-scope-twist" aria-hidden />
          )}
          <label className={`s7-scope-pick ${covered ? 'is-covered' : ''}`}>
            <input type="checkbox" checked={on} disabled={covered} onChange={() => toggle(node.id)} />
            <span>{withTrail ? trail(node) : node.title.en}</span>
            {node.title.ar && !withTrail ? (
              <span className="s7-muted" dir="rtl" lang="ar">
                {node.title.ar}
              </span>
            ) : null}
            {covered ? <span className="s7-hint">included</span> : null}
          </label>
        </div>
        {expanded && !withTrail && kids.length ? <ul>{kids.map((kid) => row(kid, depth + 1, false))}</ul> : null}
      </li>
    )
  }

  return (
    <div className="s7-scope">
      <div className="s7-bar" style={{ marginBottom: 0 }}>
        <SearchBox value={search} onChange={setSearch} placeholder="Find a grade, subject or chapter…" />
        <span className="s7-hint" style={{ whiteSpace: 'nowrap' }}>
          {chosen.length === 0 ? 'Nothing chosen' : chosen.length === 1 ? '1 part chosen' : `${chosen.length} parts chosen`}
        </span>
      </div>
      <ul className="s7-scope-tree">
        {query ? (matches.length ? matches.map((node) => row(node, 0, true)) : <li className="s7-hint">Nothing called that.</li>) : (children.get(null) ?? []).map((node) => row(node, 0, false))}
      </ul>
    </div>
  )
}

// ---------------------------------------------------------------------------
// The password, set by the admin
// ---------------------------------------------------------------------------

/**
 * The password an admin sets for a member (decided 2026-09-26: no setup link, no activation — the
 * member signs in with it straight away). Shown while generated, because the next thing the admin
 * does is pass it on.
 */
export function PasswordField({
  value,
  onChange,
  rules,
  username,
  error,
  label = 'Password',
}: {
  value: string
  onChange: (value: string) => void
  rules: PasswordRules
  username: string
  error: string | null
  label?: string
}) {
  const [reveal, setReveal] = useState(false)
  const missing = staffPasswordProblems(value, username, rules)

  return (
    <Field
      label={label}
      error={error}
      hint={
        error
          ? undefined
          : !value
            ? `At least ${rules.minimumLength} characters, with an upper-case letter, a lower-case letter and a digit. Or generate one.`
            : missing.length
              ? `Needs ${missing.join(', ')}.`
              : 'Meets the rules. You will see it once more after saving, to pass on.'
      }
    >
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
        <div className="s7-auth-reveal" style={{ flex: '1 1 auto' }}>
          <Input
            type={reveal ? 'text' : 'password'}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            invalid={!!error}
            // Not the admin's own password: no browser should offer to save it as theirs.
            autoComplete="new-password"
            mono={reveal}
          />
          <button
            type="button"
            onClick={() => setReveal((v) => !v)}
            aria-label={reveal ? 'Hide password' : 'Show password'}
            title={reveal ? 'Hide password' : 'Show password'}
            tabIndex={-1}
          >
            {reveal ? <EyeOff size={15} /> : <Eye size={15} />}
          </button>
        </div>
        <Button
          variant="ghost"
          onClick={() => {
            onChange(generatePassword(Math.max(14, rules.minimumLength + 2)))
            setReveal(true)
          }}
        >
          <WandSparkles size={15} /> Generate
        </Button>
      </div>
    </Field>
  )
}

// ---------------------------------------------------------------------------
// What to hand over
// ---------------------------------------------------------------------------

/**
 * Everything the member needs, in one place: the Studio's address — the same one for every member,
 * whatever their role — their username, and the password. The password is shown this once.
 */
export function CredentialsHandover({
  name,
  username,
  password,
  address,
}: {
  name: string
  username: string
  password: string
  address: string
}) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(`Content Studio: ${address}\nUsername: ${username}\nPassword: ${password}`)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1500)
    } catch {
      // Refused clipboard: everything is on screen to copy by hand.
    }
  }

  return (
    <div className="s7-stack">
      <Note>
        Give these to {name}. They sign in at the Content Studio’s address — the same one for everybody in the content team — and can
        start straight away. This is the only time the password is shown.
      </Note>

      <DefList>
        <Def label="Content Studio">
          <code className="s7-key" style={{ overflowWrap: 'anywhere' }}>
            {address}
          </code>
        </Def>
        <Def label="Username">
          <code className="s7-key">{username}</code>
        </Def>
        <Def label="Password">
          <code className="s7-key">{password}</code>
        </Def>
      </DefList>

      <div>
        <Button variant="ghost" onClick={() => void copy()}>
          {copied ? <Check size={15} /> : <Copy size={15} />}
          {copied ? 'Copied' : 'Copy all three'}
        </Button>
      </div>
    </div>
  )
}
