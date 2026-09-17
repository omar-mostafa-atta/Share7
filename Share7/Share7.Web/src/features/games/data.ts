import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type { GameAdminDto, SaveGameRequest } from '../../types/api'

// ===========================================================================
// Games — data access
//
// Ports wwwroot/js/games.js. The endpoints are unchanged; what is new is that
// the list is authoring-shaped. GET /api/admin/games returns GameAdminDto,
// which carries every translation and every matchmaking flag, so the table can
// show a game's real configuration instead of just its key.
// ===========================================================================

export function useGames() {
  const resource = useResourceList<GameAdminDto>('/api/admin/games')

  const create = useCallback(
    async (request: SaveGameRequest) => {
      const created = await api.post<GameAdminDto>('/api/admin/games', request)
      toast.success('Game created', `"${request.gameKey}" is in the catalogue.`)
      await resource.reload()
      return created
    },
    [resource],
  )

  const update = useCallback(
    async (gameId: string, request: SaveGameRequest) => {
      const updated = await api.put<GameAdminDto>(`/api/admin/games/${gameId}`, request)

      // Replace in place. A full reload would remount every row and replay the
      // entrance animation for a change to one of them.
      resource.set((rows) => rows.map((g) => (g.gameId === gameId ? updated : g)))

      toast.success(
        request.isActive ? 'Game updated' : 'Game deactivated',
        request.isActive
          ? `"${updated.gameKey}" saved.`
          : `"${updated.gameKey}" is hidden from clients. Existing runs and results are untouched.`,
      )

      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (game: GameAdminDto) => {
      await api.del(`/api/admin/games/${game.gameId}`)
      resource.set((rows) => rows.filter((g) => g.gameId !== game.gameId))
      toast.success('Game deleted', `"${game.gameKey}" is gone.`)
    },
    [resource],
  )

  return { ...resource, games: resource.data, create, update, remove }
}

/** A blank game, matching the C# defaults on SaveGameRequest so a create that
 *  touches nothing produces the same record the API would. */
export function blankGame(): SaveGameRequest {
  return {
    gameKey: '',
    minPlayers: 1,
    maxPlayers: 2,
    readyTimeoutSeconds: 20,
    supportsSinglePlayer: true,
    supportsMultiplayer: true,
    useLobby: true,
    useMatchmaking: true,
    isActive: true,
    translations: [],
  }
}

/** GameAdminDto back to the request shape its editor works on. */
export function toRequest(game: GameAdminDto): SaveGameRequest {
  return {
    gameKey: game.gameKey,
    minPlayers: game.minPlayers,
    maxPlayers: game.maxPlayers,
    readyTimeoutSeconds: game.readyTimeoutSeconds,
    supportsSinglePlayer: game.supportsSinglePlayer,
    supportsMultiplayer: game.supportsMultiplayer,
    useLobby: game.useLobby,
    useMatchmaking: game.useMatchmaking,
    isActive: game.isActive,
    translations: game.translations.map((t) => ({ ...t })),
  }
}
