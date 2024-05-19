import { BrowserRouter, Routes, Route } from 'react-router-dom'
import Chat from './components/Chat'

function App() {
  return (
    <>
    <BrowserRouter>
       // later put a navbar component here here that offers links to every available view, since that's outside the routes it'll appear for all of them
        <Routes>
            <Route path='chat' element={<Chat />}></Route>
        </Routes>

    </BrowserRouter>
     
    </>
  )
}

export default App
