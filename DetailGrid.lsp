;;; ---------------------------------------------------------------------
;;; DetailGrid.lsp
;;;
;;; Click a frame block (the block you use to line up viewports with a
;;; title block) and automatically insert a "grid" block on top of it,
;;; matched to the frame's insertion point, rotation, and scale - so the
;;; grid is always aligned no matter what sheet scale the frame is at.
;;;
;;; Commands:
;;;   DETAILGRID  (alias DG)  - click a frame block, insert/refresh its grid
;;;   DGRIDBLOCK              - click a block instance to use as the grid
;;;                             block from now on (instead of the default)
;;;
;;; Setup:
;;;   1. Draw your grid module as a block (default expected name:
;;;      GRID_LINE) at true 1:1 size, with its insertion point at the
;;;      corner/center you want anchored to the frame's insertion point.
;;;      Insert it into the drawing at least once (or build it in the
;;;      block editor) so it exists in the block table.
;;;   2. If you named it something other than GRID_LINE, run DGRIDBLOCK
;;;      once and click an instance of it - or edit *dg:grid-block* below.
;;;
;;; Usage:
;;;   Run DETAILGRID, click the frame block. The grid is inserted on the
;;;   DETAIL-GRID layer (created automatically, non-plotting). Running
;;;   DETAILGRID again on the same frame replaces its grid instead of
;;;   stacking up extra copies.
;;;
;;; Load: APPLOAD this file, or add (load "DetailGrid.lsp") to your
;;; acaddoc.lsp / Startup Suite to have it available in every drawing.
;;; ---------------------------------------------------------------------

(vl-load-com)

;; ---- configuration -----------------------------------------------------
;; Name of the block to insert as the grid. Change this, or use DGRIDBLOCK
;; to set it interactively by clicking an instance of the block you want.
(if (not *dg:grid-block*) (setq *dg:grid-block* "GRID_LINE"))

;; Layer the grid is inserted on (created automatically, non-plotting).
(setq *dg:grid-layer* "DETAIL-GRID")

;; XDATA application name used to remember which grid belongs to which frame.
(setq *dg:xdata-app* "DETAILGRID")

;; ---- helpers -------------------------------------------------------------

(defun dg:ensure-app-registered ()
  (if (not (tblsearch "APPID" *dg:xdata-app*))
    (regapp *dg:xdata-app*)
  )
)

(defun dg:ensure-layer ( / doc lyr)
  (if (not (tblsearch "LAYER" *dg:grid-layer*))
    (progn
      (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
      (setq lyr (vla-Add (vla-get-Layers doc) *dg:grid-layer*))
      (vla-put-Color lyr 6)
      (vl-catch-all-apply 'vla-put-Plottable (list lyr :vlax-false))
    )
  )
)

;; Look up the current annotation scale factor (drawing-units / paper-units)
;; for the scale named in CANNOSCALE.
(defun dg:get-annoscale-factor ( / doc name factor pu du)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq name (getvar "CANNOSCALE"))
  (setq factor 1.0)
  (vlax-for sc (vla-get-Scales doc)
    (if (= (vla-get-Name sc) name)
      (progn
        (setq pu (vla-get-PaperUnits sc))
        (setq du (vla-get-DrawingUnits sc))
        (if (/= pu 0) (setq factor (/ du pu)))
      )
    )
  )
  factor
)

;; Returns (xscale . yscale) to apply to the grid block so it matches the
;; frame block's effective scale.
(defun dg:get-frame-scale (frameObj / sx sy)
  (if (and (vlax-property-available-p frameObj 'Annotative)
           (= (vla-get-Annotative frameObj) :vlax-true))
    (progn
      (setq sx (dg:get-annoscale-factor))
      (setq sy sx)
    )
    (progn
      (setq sx (vla-get-XScaleFactor frameObj))
      (setq sy (vla-get-YScaleFactor frameObj))
    )
  )
  (cons sx sy)
)

;; Click-select a block reference. Returns its vla-object, or the symbol
;; 'cancel if the user backs out.
(defun dg:select-frame ( / ent obj etype result)
  (setq result nil)
  (while (not result)
    (setq ent (car (entsel "\nSelect (click) the frame block, or press Enter to cancel: ")))
    (cond
      ((null ent) (setq result 'cancel))
      (t
        (setq etype (cdr (assoc 0 (entget ent))))
        (if (= etype "INSERT")
          (setq result (vlax-ename->vla-object ent))
          (princ "\nThat's not a block reference - try again.")
        )
      )
    )
  )
  result
)

;; Read the handle of the grid block previously linked to this frame, if any.
(defun dg:get-linked-handle (ent / elist xdlist apppair h)
  (setq elist (entget ent (list *dg:xdata-app*)))
  (setq xdlist (cdr (assoc -3 elist)))
  (setq apppair (assoc *dg:xdata-app* xdlist))
  (setq h nil)
  (if apppair
    (foreach code (cdr apppair)
      (if (= (car code) 1000) (setq h (cdr code)))
    )
  )
  h
)

;; Store the grid block's handle on the frame entity as xdata.
(defun dg:set-xdata (ent handle)
  (entmod
    (append (entget ent) (list (list -3 (list *dg:xdata-app* (cons 1000 handle)))))
  )
)

;; Erase whatever grid block is currently linked to this frame, if it still exists.
(defun dg:remove-linked-grid (frameObj / ent h oldEnt)
  (setq ent (vlax-vla-object->ename frameObj))
  (setq h (dg:get-linked-handle ent))
  (if h
    (progn
      (setq oldEnt (handent h))
      (if (and oldEnt (entget oldEnt))
        (entdel oldEnt)
      )
    )
  )
)

;; ---- commands --------------------------------------------------------------

(defun c:DETAILGRID ( / *error* oldErr frameObj gridObj insPt rot scalePair sx sy doc ms)
  (setq oldErr *error*)
  (defun *error* (msg)
    (if (and msg (not (member msg '("Function cancelled" "quit / exit abort"))))
      (princ (strcat "\nDetailGrid error: " msg))
    )
    (setvar "CMDECHO" 1)
    (setq *error* oldErr)
    (princ)
  )

  (setvar "CMDECHO" 0)
  (dg:ensure-app-registered)
  (dg:ensure-layer)

  (if (not (tblsearch "BLOCK" *dg:grid-block*))
    (progn
      (princ (strcat "\nGrid block \"" *dg:grid-block* "\" isn't defined in this drawing."))
      (princ "\nDraw/insert it once, or run DGRIDBLOCK to point DetailGrid at a different block.")
    )
    (progn
      (setq frameObj (dg:select-frame))
      (if (eq frameObj 'cancel)
        (princ "\nCancelled.")
        (progn
          (setq insPt (vla-get-InsertionPoint frameObj))
          (setq rot (vla-get-Rotation frameObj))
          (setq scalePair (dg:get-frame-scale frameObj))
          (setq sx (car scalePair) sy (cdr scalePair))

          (dg:remove-linked-grid frameObj)

          (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
          (setq ms (vla-get-ModelSpace doc))
          (setq gridObj (vla-InsertBlock ms insPt *dg:grid-block* sx sy 1.0 rot))
          (vla-put-Layer gridObj *dg:grid-layer*)

          (dg:set-xdata (vlax-vla-object->ename frameObj) (vla-get-Handle gridObj))
          (vla-Update gridObj)

          (princ
            (strcat "\nGrid \"" *dg:grid-block* "\" aligned to frame - scale "
                    (rtos sx 2 4) ", rotation " (angtos rot) ".")
          )
        )
      )
    )
  )

  (setvar "CMDECHO" 1)
  (setq *error* oldErr)
  (princ)
)

(defun c:DG () (c:DETAILGRID))

(defun c:DGRIDBLOCK ( / ent obj name)
  (setq ent (car (entsel "\nClick an instance of the block to use as the grid block: ")))
  (if (and ent (= (cdr (assoc 0 (entget ent))) "INSERT"))
    (progn
      (setq obj (vlax-ename->vla-object ent))
      (setq name (vla-get-EffectiveName obj))
      (setq *dg:grid-block* name)
      (princ (strcat "\nDetailGrid will now use block \"" name "\" as the grid block."))
    )
    (princ "\nNo block selected - grid block unchanged.")
  )
  (princ)
)

(princ "\nDetailGrid loaded - type DETAILGRID (or DG) to align a grid to a frame block, DGRIDBLOCK to choose the grid block.")
(princ)
