;;; LoadAll.lsp - put in C:\CAD with the other .lsp files, APPLOAD this one.
;;; (Add it to APPLOAD > Startup Suite to load it every time AutoCAD starts.)
(setq LA-DIR "C:/CAD/")
(foreach f '("BlockSwap.lsp" "RunAll.lsp" "ListBlocks.lsp" "CloneBlock.lsp" "ClearAttributes.lsp")
  (if (findfile (strcat LA-DIR f))
    (progn (load (strcat LA-DIR f)) (princ (strcat "\nLoaded " f)))
    (princ (strcat "\nNot found (skipped): " LA-DIR f))))
(princ "\nCommands: BLOCKSWAP, RUNALL, LISTBLOCKS, CLONEBLOCK, CLEARATTS")
(princ)
