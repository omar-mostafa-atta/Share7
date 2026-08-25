import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import { api } from '../lib/client'
import { useAuth, tokenLanguageId } from './auth'
import type { AuthResult, Language } from '../types/api'

interface LanguageState {
  languages: Language[]
  selectedLangId: string

  load: () => Promise<Language[]>
  select: (langId: string) => void
  apply: (langId: string) => Promise<void>
}

/**
 * Content languages and the admin's current choice.
 *
 * Separate from the auth store because the lifetimes differ: the language list is public
 * reference data (GET /api/languages is [AllowAnonymous], which is why the login screen can
 * populate its picker before anyone has signed in), while tokens are session state.
 */
export const useLanguages = create<LanguageState>()(
  persist(
    (set, get) => ({
      languages: [],
      selectedLangId: '',

      load: async () => {
        // The endpoint returns a bare array, not a wrapped object.
        const languages = await api.get<Language[]>('/api/languages', { silent: true })
        set({ languages })

        // The token's claim wins over anything remembered locally. It is what the API will
        // actually resolve names in, so trusting the stored value instead is how the selector ends
        // up disagreeing with the tree underneath it — and how a question sheet gets published to
        // a language the admin did not think they were looking at.
        const fromToken = tokenLanguageId()
        if (fromToken && languages.some((l) => l.id === fromToken)) {
          set({ selectedLangId: fromToken })
        } else if (!get().selectedLangId && languages.length) {
          set({ selectedLangId: languages[0].id })
        }

        return languages
      },

      select: (langId) => set({ selectedLangId: langId }),

      /**
       * Switch the *content* language, not just the local selection.
       *
       * Only `GET /api/grades` accepts an explicit `?langId=`. Terms, subjects, chapters and
       * lessons resolve their names from the `preferred_language` claim inside the access token,
       * so changing the tree's language means changing the token — which is why this endpoint
       * returns a fresh pair. Storing the choice locally without this call would relabel the
       * grades column and leave the other four in the previous language.
       */
      apply: async (langId) => {
        const auth = await api.post<AuthResult>('/api/users/me/preferred-language', {
          languageId: langId,
        })

        if (auth.accessToken) useAuth.getState().setSession(auth)
        set({ selectedLangId: langId })
      },
    }),
    {
      name: 's7_languages',
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
)
