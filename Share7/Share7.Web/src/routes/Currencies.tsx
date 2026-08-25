import { motion } from 'motion/react'
import { Coins, RefreshCw, Wallet } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '../components/layout/AppShell'
import { Card, CardBody, CardHeader, Divider, IconButton } from '../components/ui/primitives'
import { listVariants } from '../components/ui/motion'
import { BalanceGrid, GrantForm } from '../features/currencies/BalancePanel'
import { CreateCurrencyForm } from '../features/currencies/CreateCurrencyForm'
import { CurrencyTable } from '../features/currencies/CurrencyTable'
import { EditCurrencyModal } from '../features/currencies/EditCurrencyModal'
import { useBalances, useCurrencies } from '../features/currencies/data'
import type { CurrencyDto } from '../types/api'

export function Currencies() {
  const { currencies, loading, refreshing, reload, create, update } = useCurrencies()
  const balances = useBalances()
  const [editing, setEditing] = useState<CurrencyDto | null>(null)

  return (
    <>
      <PageHeader icon={<Coins size={22} />} title="Currencies">
        Define in-game currencies and manage your own balance for testing.
      </PageHeader>

      <motion.div className="s7-grid" variants={listVariants} initial="hidden" animate="visible">
        <div className="s7-col-7">
          <Card>
            <CardHeader
              icon={<Coins size={16} />}
              title="Currencies"
              actions={
                <IconButton label="Refresh currencies" busy={refreshing} onClick={reload}>
                  <RefreshCw size={14} />
                </IconButton>
              }
            />
            <CardBody>
              <CurrencyTable currencies={currencies} loading={loading} onEdit={setEditing} />
              <Divider />
              <CreateCurrencyForm onCreate={create} />
            </CardBody>
          </Card>
        </div>

        <div className="s7-col-5">
          <Card>
            <CardHeader
              icon={<Wallet size={16} />}
              title="My balance"
              actions={
                <IconButton
                  label="Refresh balances"
                  busy={balances.refreshing}
                  onClick={balances.reload}
                >
                  <RefreshCw size={14} />
                </IconButton>
              }
            />
            <CardBody>
              <BalanceGrid balances={balances.balances} loading={balances.loading} />
              <Divider />
              <GrantForm currencies={currencies} onGrant={balances.grant} />
            </CardBody>
          </Card>
        </div>
      </motion.div>

      <EditCurrencyModal
        currency={editing}
        onClose={() => setEditing(null)}
        onSave={update}
      />
    </>
  )
}
