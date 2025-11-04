import './App.css'

function App() {
  return (
    <div className="w-[1280px] h-[640px] relative overflow-hidden">
      {/* Background - Single full image */}
      <div className="absolute inset-0">
        <img
          src="/flat_map.png"
          alt="Flat Map Mode"
          className="w-full h-full object-cover blur-md"
        />
        <div className="absolute inset-0 bg-slate-900/40"></div>
      </div>

      {/* Title Section - Overlaid on top */}
      <div className="absolute inset-0 flex flex-col items-center justify-center text-center px-16">
        {/* Background Box */}
        <div className="bg-gradient-to-br from-black/80 to-black/60 backdrop-blur-md rounded-2xl border-2 border-white/20 shadow-2xl relative" style={{ padding: '4rem 6rem' }}>
          {/* Corner Accents */}
          {/* Top Left */}
          <div className="absolute top-0 left-0">
            <div className="absolute top-0 left-0 w-24 h-1 bg-white"></div>
            <div className="absolute top-0 left-0 w-1 h-24 bg-white"></div>
          </div>
          {/* Bottom Right */}
          <div className="absolute bottom-0 right-0">
            <div className="absolute bottom-0 right-0 w-24 h-1 bg-white"></div>
            <div className="absolute bottom-0 right-0 w-1 h-24 bg-white"></div>
          </div>

          {/* Main Title */}
          <h1 className="text-9xl font-bold leading-none flex items-center justify-center gap-6" style={{ marginBottom: '1rem' }}>
            <span className="text-blue-400">Archon</span>
            <span className="text-white">Engine</span>
          </h1>

          {/* Tagline */}
          <p className="text-3xl text-gray-200 font-medium leading-relaxed">
            Grand Strategy Made Accessible
          </p>
        </div>
      </div>
    </div>
  )
}

export default App
