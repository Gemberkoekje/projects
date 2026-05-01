import { render, screen } from '@testing-library/react'
import App from '../App'

describe('App', () => {
  it('renders the top navigation bar', () => {
    render(<App />)
    expect(screen.getByRole('banner')).toBeInTheDocument()
  })

  it('renders the main navigation sidebar', () => {
    render(<App />)
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument()
  })

  it('renders all sidebar navigation links', () => {
    render(<App />)
    const labels = [
      'Overview',
      'Fleet',
      'Finance',
      'Markets',
      'Runs',
      'Universe',
      'Contracts',
      'Activity',
      'Snapshots',
      'Health',
      'Settings',
    ]
    for (const label of labels) {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument()
    }
  })

  it('renders the theme toggle button', () => {
    render(<App />)
    expect(
      screen.getByRole('button', { name: /switch to (dark|light) mode/i }),
    ).toBeInTheDocument()
  })
})
