import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom'
import { Wiping } from './board/pieces'
import { useI18n } from './i18n/i18n'
import { useSession } from './lib/session'
import { studio, type ContentLanguage } from './lib/studio'
import { Activate } from './screens/Activate'
import { Account } from './screens/Account'
import { Activity } from './screens/Activity'
import { Bank } from './screens/Bank'
import { Curriculum } from './screens/Curriculum'
import { Handbook } from './screens/Handbook'
import { Home } from './screens/Home'
import { Lesson } from './screens/Lesson'
import { Changes } from './screens/Changes'
import { Releases } from './screens/Releases'
import { Reviews } from './screens/Reviews'
import { SignIn } from './screens/SignIn'
import { Rail } from './screens/Rail'
import { FrameworkSkills, Skills } from './screens/Skills'
import { Mapping } from './screens/Mapping'
import { Quality, Question } from './screens/Quality'
import { Recovery, RecoveryAt } from './screens/Recovery'
import { Exams, OneBlueprint } from './screens/Exams'

// ===========================================================================
// The board, and the places on it
//
// Two worlds: before you are signed in (the sign-in board and the activation
// board, both standing alone with no places along the top), and after, where
// every screen is the same board — rail, work, ledge — so nothing moves from
// one place to the next.
// ===========================================================================

// ---------------------------------------------------------------------------
// The ledge
//
// The action that matters lives on the aluminium ledge at the bottom of the
// board, in sight while the work scrolls. A screen puts its own actions there
// with useLedge(); the layout is what draws it, so it is always the same ledge.
// ---------------------------------------------------------------------------

const LedgeContext = createContext<((content: ReactNode) => void) | null>(null)

export function useLedge(content: ReactNode, keys: unknown[]) {
  const set = useContext(LedgeContext)

  useEffect(() => {
    set?.(content)
    return () => set?.(null)
    // The content is rebuilt every render; the keys are what actually change it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [set, ...keys])
}

// ---------------------------------------------------------------------------
// The content languages
//
// Read once and shared: every screen needs them to put a name on a column, and
// they change about as often as the curriculum itself.
// ---------------------------------------------------------------------------

const LanguagesContext = createContext<ContentLanguage[]>([])

export function useLanguages() {
  return useContext(LanguagesContext)
}

/** The languages a lesson must have before it can be released. */
export function useRequiredLanguages() {
  const languages = useLanguages()
  return useMemo(() => languages.filter((one) => one.isContentLanguage && one.requiredToPublish), [languages])
}

/** The languages questions may be written in, in the order the editor lays them out. */
export function useContentLanguages() {
  const languages = useLanguages()
  return useMemo(
    () => languages.filter((one) => one.isContentLanguage).sort((a, b) => a.sortOrder - b.sortOrder),
    [languages],
  )
}

// ---------------------------------------------------------------------------

export function App() {
  const session = useSession()
  const location = useLocation()
  const { t } = useI18n()

  // The activation board is reached from a setup link and must open whether or
  // not somebody is signed in on this browser.
  if (location.pathname === '/activate') return <Activate />

  if (session.status === 'starting') {
    return (
      <div className="board">
        <main className="sheet" aria-busy="true">
          <span className="sr-only">{t('common.loading')}</span>
          <Wiping rows={4} />
        </main>
      </div>
    )
  }

  if (session.status === 'signed-out') return <SignIn reason={session.reason} />

  return <Studio />
}

function Studio() {
  const [ledge, setLedge] = useState<ReactNode>(null)
  const [languages, setLanguages] = useState<ContentLanguage[]>([])
  const { t } = useI18n()

  useEffect(() => {
    let alive = true
    studio
      .languages()
      .then((list) => {
        if (alive) setLanguages(list)
      })
      .catch(() => {
        // The board still works without them; names fall back to whatever a node has.
      })
    return () => {
      alive = false
    }
  }, [])

  const set = useCallback((content: ReactNode) => setLedge(content), [])

  return (
    <LanguagesContext.Provider value={languages}>
      <LedgeContext.Provider value={set}>
        <div className="board">
          <a className="act small sr-only" href="#work">
            {t('common.skipToWork')}
          </a>
          <Rail />
          <main className="sheet" id="work">
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/curriculum" element={<Curriculum />} />
              <Route path="/curriculum/:nodeId" element={<Curriculum />} />
              <Route path="/lessons/:lessonId" element={<Lesson />} />
              <Route path="/drafts/:draftId" element={<Changes />} />
              <Route path="/bank" element={<Bank />} />
              <Route path="/skills" element={<Skills />} />
              <Route path="/skills/:frameworkId" element={<FrameworkSkills />} />
              <Route path="/mapping/:subjectNodeId" element={<Mapping />} />
              <Route path="/quality" element={<Quality />} />
              <Route path="/quality/:itemId" element={<Question />} />
              <Route path="/exams" element={<Exams />} />
              <Route path="/exams/:blueprintId" element={<OneBlueprint />} />
              <Route path="/recovery" element={<Recovery />} />
              <Route path="/recovery/:nodeId" element={<RecoveryAt />} />
              <Route path="/reviews" element={<Reviews />} />
              <Route path="/releases" element={<Releases />} />
              <Route path="/releases/:releaseId" element={<Releases />} />
              <Route path="/activity" element={<Activity />} />
              <Route path="/account" element={<Account />} />
              <Route path="/handbook" element={<Handbook />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </main>
          <footer className="ledge">{ledge}</footer>
        </div>
      </LedgeContext.Provider>
    </LanguagesContext.Provider>
  )
}

/** One place chalked along the top of the board. */
export function Place({ to, children, count }: { to: string; children: ReactNode; count?: number }) {
  return (
    <NavLink to={to} end={to === '/'} className="place">
      {children}
      {count ? <span className="place-count num">{count}</span> : null}
    </NavLink>
  )
}
