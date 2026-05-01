import { render, screen } from '@testing-library/react'
import App from '../App'

describe('App', () => {
  it('renders the SpaceTraders WebUI heading', () => {
    render(<App />)
    expect(screen.getByText('SpaceTraders WebUI')).toBeInTheDocument()
  })
})
